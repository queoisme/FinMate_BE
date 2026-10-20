"""Kiến thức nhận diện bill (thương hiệu, từ khóa danh mục) lấy từ PostgreSQL, và ghi bill lạ vào DB.

- Khởi động API: nếu DB có dữ liệu thì thay các danh sách trong code (cập nhật tại chỗ nên mọi module vẫn dùng được); DB trống thì giữ bản trong code.
- Nạp dữ liệu ban đầu và bổ sung mục mới của data/*.json: scripts/seed_knowledge_to_pg.py (chỉ thêm, DB luôn thắng). Sau đó sửa trong DB (thêm dòng, đặt is_active = false, KHÔNG xóa dòng vì seed sẽ thêm lại) rồi gọi reload để áp dụng.
- ponytail: reload cập nhật tại chỗ nên một yêu cầu đang chạy đúng lúc có thể lỗi hiếm gặp; reload khi vắng người hoặc khởi động lại.
"""
import hashlib
import logging
import os
import uuid
from datetime import datetime, timezone

from classification import brands
from classification import chat_vocab
from classification import data_files
from parsing import extract
from storage.memory import _key
from storage.storage_pg import _cursor, _who, connect_from_env


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


_KEYWORD_FILES = (("merchant", "merchant_keywords.json"), ("product", "product_keywords.json"), ("chat", "chat_keywords.json"))


def _defaults() -> tuple[list[tuple], list[tuple]]:
    """Bản mặc định đọc thẳng từ data/*.json: (thương hiệu, từ khóa). Không đọc các bảng đang chạy vì apply() có thể đã thay chúng bằng DB."""
    data = data_files.load("brands.json")
    whole = set(data["whole_word"])
    brand_rows = [(b["key"], b["category"], b["display"], b["key"] in whole) for b in data["brands"]]
    keyword_rows = [(kind, category, word, (rank + 1) * 10)
                    for kind, name in _KEYWORD_FILES
                    for rank, (category, words) in enumerate(data_files.keyword_table(name).items())
                    for word in words]
    return brand_rows, keyword_rows


def _known(cur) -> tuple[set, set]:
    """(thương hiệu, (loại, từ khóa)) đã có dòng trong DB, kể cả dòng đã tắt hay đã chuyển sang nhóm khác: đó là quyết định của DB, không phải thiếu."""
    cur.execute("SELECT brand_key FROM bill_brands")
    brand_keys = {r[0] for r in cur.fetchall()}
    cur.execute("SELECT kind, keyword FROM bill_keywords")  # không xét nhóm: từ đã được chuyển nhóm (migration, người duyệt) thì không thêm lại ở nhóm cũ
    return brand_keys, {tuple(r) for r in cur.fetchall()}


def missing(connect) -> dict:
    """Mục có trong data/*.json mà DB chưa có (xem _known). Đây là phần seed() sẽ thêm."""
    brand_rows, keyword_rows = _defaults()
    with _cursor(connect) as cur:
        have_brands, have_keywords = _known(cur)
    return {"brands": [key for key, *_ in brand_rows if key not in have_brands],
            "keywords": [(kind, category, word) for kind, category, word, _ in keyword_rows if (kind, word) not in have_keywords]}


def seed(connect) -> dict:
    """Thêm vào DB những mục data/*.json còn thiếu (chạy lại an toàn: dòng đã có, đã sửa, đã tắt hay từ khóa đã chuyển nhóm đều giữ nguyên, không bao giờ ghi đè). Trả về số dòng mới."""
    added = {"brands": 0, "keywords": 0}
    brand_rows, keyword_rows = _defaults()
    with _cursor(connect) as cur:
        _, have_keywords = _known(cur)
        for key, category, display, whole in brand_rows:
            cur.execute(
                "INSERT INTO bill_brands (id, brand_key, category_slug, display_name, whole_word, is_active, source, created_at, updated_at) "
                "VALUES (%s, %s, %s, %s, %s, TRUE, 'seed', %s, %s) ON CONFLICT (brand_key) DO NOTHING",
                (str(uuid.uuid4()), key, category, display, whole, _now(), _now()))
            added["brands"] += cur.rowcount if cur.rowcount and cur.rowcount > 0 else 0
        for kind, category, word, priority in keyword_rows:
            if (kind, word) in have_keywords:
                continue
            cur.execute(
                "INSERT INTO bill_keywords (id, kind, category_slug, keyword, category_priority, is_active, source, created_at) "
                "VALUES (%s, %s, %s, %s, %s, TRUE, 'seed', %s) ON CONFLICT (kind, category_slug, keyword) DO NOTHING",
                (str(uuid.uuid4()), kind, category, word, priority, _now()))
            added["keywords"] += cur.rowcount if cur.rowcount and cur.rowcount > 0 else 0
    return added


def load(connect) -> dict | None:
    """Đọc kiến thức đang bật từ DB; DB chưa có gì thì None."""
    with _cursor(connect) as cur:
        cur.execute("SELECT brand_key, category_slug, display_name, whole_word FROM bill_brands WHERE is_active ORDER BY brand_key")
        brand_rows = cur.fetchall()
        cur.execute("SELECT kind, category_slug, keyword, category_priority FROM bill_keywords WHERE is_active "
                    "ORDER BY category_priority, category_slug, keyword")
        keyword_rows = cur.fetchall()
    if not brand_rows and not keyword_rows:
        return None
    by_kind: dict[str, dict[str, list[str]]] = {"merchant": {}, "product": {}, "chat": {}}
    for kind, category, word, _ in keyword_rows:  # đã sắp theo category_priority: giữ đúng thứ tự ưu tiên giữa các nhóm
        by_kind.setdefault(kind, {}).setdefault(category, []).append(word)
    return {"brands": {k: (c, d, bool(w)) for k, c, d, w in brand_rows},
            **{kind: {c: tuple(w) for c, w in table.items()} for kind, table in by_kind.items()}}


def apply(knowledge: dict) -> None:
    """Thay nội dung các danh sách trong code bằng dữ liệu từ DB (tại chỗ). Phần nào DB không có thì giữ bản trong code."""
    if knowledge["brands"]:
        for target in (brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS):
            target.clear()
        for key, (category, display, whole) in knowledge["brands"].items():
            brands.BRAND_CATEGORIES[key], brands.BRAND_DISPLAY[key] = category, display
            if whole:
                brands.WHOLE_WORD_BRANDS.add(key)
    for new, table in ((knowledge["merchant"], extract._CATEGORY_KEYWORDS), (knowledge["product"], extract._PRODUCT_KEYWORDS),
                       (knowledge.get("chat"), chat_vocab.CHAT_KEYWORDS)):
        if new:
            table.clear()
            table.update(new)


def reload(connect) -> bool:
    knowledge = load(connect)
    if knowledge is None:
        return False
    apply(knowledge)
    return True


def open_knowledge():
    """(nơi ghi bill lạ, hàm reload kiến thức). Có DB: nạp kiến thức từ DB ngay khi khởi động; không có DB: giữ bản trong code, không ghi gì."""
    connect = connect_from_env()
    if not connect:
        return NoUnknownScans(), lambda: False
    try:
        reload(connect)
    except Exception:  # noqa: BLE001 - DB chưa thiết lập (chưa chạy scripts/db_setup.py) không được làm API chết lúc khởi động
        logging.getLogger(__name__).warning("Chưa nạp được kiến thức từ DB (đã chạy scripts/db_setup.py chưa?): dùng bản trong code", exc_info=True)
    return UnknownScans(connect, os.environ.get("FINMATE_USER_HASH", "local")), lambda: reload(connect)


class UnknownScans:
    """Ghi bill lạ vào bill_unknown_scans để người duyệt thêm vào bảng thương hiệu."""

    def __init__(self, connect, user_hash: str):
        self._connect, self._user = connect, user_hash

    def record(self, merchant: str | None, lines: list[str], total: int | None, predicted_category: str | None,
               predicted_source: str | None, confidence: float | None) -> None:
        key = _key(merchant or "")
        content_hash = hashlib.sha256("\n".join([key, str(total), *lines]).encode("utf-8")).hexdigest()
        now = _now()
        with _cursor(self._connect) as cur:
            cur.execute(
                "INSERT INTO bill_unknown_scans (id, user_id_hash, merchant_read, merchant_key, predicted_category, predicted_source, confidence, "
                "total_amount, lines, content_hash, seen_count, status, first_seen_at, last_seen_at) "
                "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, 1, 'new', %s, %s) "
                "ON CONFLICT (content_hash) DO UPDATE SET seen_count = bill_unknown_scans.seen_count + 1, last_seen_at = excluded.last_seen_at",
                (str(uuid.uuid4()), _who(self._user), merchant, key, predicted_category, predicted_source, confidence, total, "\n".join(lines),
                 content_hash, now, now))

    def mark_labeled(self, merchant: str | None) -> None:
        """Người dùng đã chốt danh mục cho cửa hàng này: các bill lạ cùng tên không cần xem xét nữa."""
        key = _key(merchant or "")
        if key:
            with _cursor(self._connect) as cur:
                cur.execute("UPDATE bill_unknown_scans SET status = 'labeled' WHERE merchant_key = %s AND status = 'new'", (key,))


class NoUnknownScans:
    """Không có DB thì không ghi gì (bản chạy bằng file như cũ)."""

    def record(self, *args, **kwargs) -> None:
        pass

    def mark_labeled(self, merchant) -> None:
        pass
