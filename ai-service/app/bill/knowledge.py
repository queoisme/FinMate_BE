"""Kiến thức nhận diện hóa đơn lấy từ DB (``bill_brands``, ``bill_keywords``) và cửa hàng người dùng đã chốt (``bill_user_merchant_category``).

Bản trong code (``app/bill/brands.py`` và các bảng trong ``classifier.py``) là mặc định và là đường lui. Có dữ liệu trong DB thì nó THAY các bảng
đó tại chỗ, nên sửa thương hiệu / từ khóa trong DB là có hiệu lực (trong tối đa ``ttl`` giây) mà không phải build lại. Phần nào DB không có thì giữ
bản trong code. DB lỗi thì giữ bản đang dùng và ghi cảnh báo: phân loại không được chết vì DB.

Giống ``model_registry`` của pipeline thông báo (hot reload 60 giây), không giữ trạng thái nào ngoài các bảng đã nạp.
"""
from __future__ import annotations

import time
from collections.abc import Callable
from typing import NamedTuple, Protocol

import structlog
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.bill import brands, classifier
from app.services import category_resolver
from app.db.session import AsyncSessionLocal

logger = structlog.get_logger(__name__)

_DEFAULT_TTL_SECONDS = 60.0

_BRANDS_SQL = text("SELECT brand_key, category_slug, display_name, whole_word FROM bill_brands WHERE is_active ORDER BY brand_key")
# Đã sắp theo category_priority: giữ đúng thứ tự ưu tiên giữa các nhóm như trong code.
_KEYWORDS_SQL = text(
    "SELECT kind, category_slug, keyword FROM bill_keywords WHERE is_active AND kind IN ('merchant', 'product') "
    "ORDER BY category_priority, category_slug, keyword"
)
# Hai nơi nhớ cửa hàng: bảng mới lưu slug backend người dùng đã chọn khi lưu giao dịch (xem bill_user_merchant_choice), bảng cũ lưu 9 nhóm của bộ luật.
_CHOICE_SQL = text("SELECT category_slug FROM bill_user_merchant_choice WHERE user_id_hash = :user AND merchant_key = :key")
_LEGACY_SQL = text("SELECT category_slug FROM bill_user_merchant_category WHERE user_id_hash = :user AND merchant_key = :key")
_LEARN_SQL = text(
    "INSERT INTO bill_user_merchant_choice (user_id_hash, merchant_key, display_name, category_slug) "
    "VALUES (:user, :key, :name, :slug) "
    "ON CONFLICT (user_id_hash, merchant_key) DO UPDATE SET category_slug = EXCLUDED.category_slug, "
    "display_name = COALESCE(EXCLUDED.display_name, bill_user_merchant_choice.display_name), updated_at = now()"
)


_ITEM_CACHE_GET_SQL = text("SELECT item_key, category_slug FROM bill_item_cache WHERE item_key = ANY(:keys)")
# Câu trả lời ĐẦU TIÊN thắng: món đã có trong bảng thì không gọi Gemini nữa, chỉ hai request ghi cùng lúc mới vào nhánh này.
_ITEM_CACHE_PUT_SQL = text(
    "INSERT INTO bill_item_cache (item_key, category_slug) VALUES (:key, :slug) "
    "ON CONFLICT (item_key) DO UPDATE SET hits = bill_item_cache.hits + 1, updated_at = now()"
)


def merchant_key(name: str) -> str:
    """Tên cửa hàng bỏ dấu, chữ thường, gọn khoảng trắng: đúng khóa mà ocr-service lưu (``memory._key``)."""
    return " ".join(classifier._norm(name).split())


class Remembered(NamedTuple):
    category: str
    # True: slug backend người dùng đã chọn khi lưu giao dịch (bảng mới). False: nhóm trong 9 nhóm của bộ luật bill (bảng cũ).
    backend_slug: bool


class BillKnowledge(Protocol):
    async def refresh_if_stale(self) -> None: ...

    async def remembered(self, user_id_hash: str, merchant_name: str | None) -> Remembered | None:
        """Danh mục người dùng này đã chốt cho cửa hàng, hoặc None."""
        ...

    async def learn(self, user_id_hash: str, merchant_name: str, category_slug: str) -> bool:
        """Ghi nhớ người dùng đã chọn ``category_slug`` cho cửa hàng này. False khi tên không dùng được làm khóa."""
        ...

    async def cached_items(self, keys: list[str]) -> dict[str, str]:
        """Nhóm (slug backend) đã nhớ cho từng khóa tên món (``item_key``); khóa chưa có thì vắng mặt. Lỗi DB thì trả {}."""
        ...

    async def remember_items(self, answers: dict[str, str]) -> None:
        """Ghi các câu trả lời của Gemini (khóa -> slug). Không gắn người dùng. Lỗi DB chỉ cảnh báo."""
        ...


def apply_rows(brand_rows: list, keyword_rows: list) -> None:
    """Thay các bảng trong code bằng dữ liệu từ DB (tại chỗ). Phần nào DB không có thì giữ bản trong code.

    Đồng bộ (không await) nên một request khác không bao giờ thấy bảng nạp dở.
    """
    if brand_rows:
        for target in (brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS):
            target.clear()
        for key, category, display, whole in brand_rows:
            brands.BRAND_CATEGORIES[key], brands.BRAND_DISPLAY[key] = category, display
            if whole:
                brands.WHOLE_WORD_BRANDS.add(key)

    by_kind: dict[str, dict[str, list[str]]] = {"merchant": {}, "product": {}}
    for kind, category, keyword in keyword_rows:
        by_kind[kind].setdefault(category, []).append(keyword)
    for kind, table in (("merchant", classifier._CATEGORY_KEYWORDS), ("product", classifier._PRODUCT_KEYWORDS)):
        if by_kind[kind]:
            table.clear()
            table.update({category: tuple(words) for category, words in by_kind[kind].items()})
    category_resolver.invalidate()  # bộ phân loại chung dựng chỉ mục từ các bảng trên: lần dùng kế tiếp xây lại


class DbBillKnowledge:
    def __init__(
        self,
        session_factory: async_sessionmaker[AsyncSession] = AsyncSessionLocal,
        ttl: float = _DEFAULT_TTL_SECONDS,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._session_factory = session_factory
        self._ttl = ttl
        self._clock = clock
        self._loaded_at: float | None = None

    def mark_stale(self) -> None:
        """Luật vừa đổi qua API quản trị: lần ``refresh_if_stale`` kế tiếp nạp lại ngay."""
        self._loaded_at = None

    async def refresh_if_stale(self) -> None:
        now = self._clock()
        if self._loaded_at is not None and now - self._loaded_at < self._ttl:
            return
        self._loaded_at = now  # đặt trước khi nạp: DB hỏng không làm mọi request cùng thử lại
        try:
            async with self._session_factory() as session:
                brand_rows = (await session.execute(_BRANDS_SQL)).all()
                keyword_rows = (await session.execute(_KEYWORDS_SQL)).all()
        except Exception as error:  # noqa: BLE001 - DB lỗi không được làm phân loại chết: giữ bản đang dùng
            logger.warning("bill_knowledge_refresh_failed", error=type(error).__name__)
            return
        apply_rows([tuple(r) for r in brand_rows], [tuple(r) for r in keyword_rows])

    async def remembered(self, user_id_hash: str, merchant_name: str | None) -> Remembered | None:
        key = merchant_key(merchant_name or "")
        if not key:
            return None
        params = {"user": user_id_hash, "key": key}
        try:
            async with self._session_factory() as session:
                chosen = (await session.execute(_CHOICE_SQL, params)).first()
                if chosen:
                    return Remembered(chosen[0], True)
                legacy = (await session.execute(_LEGACY_SQL, params)).first()
        except Exception as error:  # noqa: BLE001
            logger.warning("bill_memory_lookup_failed", error=type(error).__name__)
            return None
        return Remembered(legacy[0], False) if legacy else None

    async def learn(self, user_id_hash: str, merchant_name: str, category_slug: str) -> bool:
        key = merchant_key(merchant_name)
        if not key:
            return False
        async with self._session_factory() as session:  # lỗi DB để nổi lên: người gọi cần biết là chưa nhớ được
            await session.execute(
                _LEARN_SQL, {"user": user_id_hash, "key": key, "name": merchant_name.strip()[:200], "slug": category_slug}
            )
            await session.commit()
        return True


    async def cached_items(self, keys: list[str]) -> dict[str, str]:
        if not keys:
            return {}
        try:
            async with self._session_factory() as session:
                rows = (await session.execute(_ITEM_CACHE_GET_SQL, {"keys": keys})).all()
        except Exception as error:  # noqa: BLE001 - bảng nhớ chỉ là đường tắt: lỗi thì để Gemini/luật lo
            logger.warning("bill_item_cache_read_failed", error=type(error).__name__)
            return {}
        return {key: slug for key, slug in rows}

    async def remember_items(self, answers: dict[str, str]) -> None:
        if not answers:
            return
        try:
            async with self._session_factory() as session:
                for key, slug in answers.items():
                    await session.execute(_ITEM_CACHE_PUT_SQL, {"key": key, "slug": slug})
                await session.commit()
        except Exception as error:  # noqa: BLE001 - không nhớ được thì lần sau hỏi lại, không được làm hỏng kết quả
            logger.warning("bill_item_cache_write_failed", error=type(error).__name__)


class NoBillKnowledge:
    """Không có DB: chỉ dùng bản trong code, không nhớ gì."""

    async def refresh_if_stale(self) -> None:
        return None

    async def remembered(self, user_id_hash: str, merchant_name: str | None) -> Remembered | None:
        return None

    async def learn(self, user_id_hash: str, merchant_name: str, category_slug: str) -> bool:
        return False

    async def cached_items(self, keys: list[str]) -> dict[str, str]:
        return {}

    async def remember_items(self, answers: dict[str, str]) -> None:
        return None
