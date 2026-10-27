"""Kiến thức nhận diện bill (thương hiệu, từ khóa danh mục) lấy từ AI Service, và ghi bill lạ qua AI Service. (Bản trước kết nối thẳng PostgreSQL: ``knowledge_pg.py``.)

- Khởi động API: nếu AI Service có dữ liệu thì thay các danh sách trong code (cập nhật tại chỗ nên mọi module vẫn dùng được); trống hoặc không gọi được thì giữ bản trong code.
- Nạp dữ liệu ban đầu và bổ sung mục mới của data/*.json: scripts/seed_knowledge.py (chỉ thêm, kiến thức đã có luôn thắng). Sau đó sửa qua trang quản trị của AI Service (thêm dòng, đặt is_active = false,
  KHÔNG xóa dòng vì seed sẽ thêm lại) rồi gọi reload để áp dụng.
- ponytail: reload cập nhật tại chỗ nên một yêu cầu đang chạy đúng lúc có thể lỗi hiếm gặp; reload khi vắng người hoặc khởi động lại.
"""
import hashlib
import logging
import os

from classification import brands
from classification import data_files
from parsing import extract
from storage.ai_api import AiApi, AiApiError, api_from_env, who
from storage.memory import _key

_KEYWORD_FILES = (("merchant", "merchant_keywords.json"), ("product", "product_keywords.json"))  # từ điển chat thuộc ai-service (tài liệu chat_dictionary), OCR không dùng


def _defaults() -> tuple[list[tuple], list[tuple]]:
    """Bản mặc định đọc thẳng từ data/*.json: (thương hiệu, từ khóa). Không đọc các bảng đang chạy vì apply() có thể đã thay chúng bằng bản của AI Service."""
    data = data_files.load("brands.json")
    whole = set(data["whole_word"])
    brand_rows = [(b["key"], b["category"], b["display"], b["key"] in whole) for b in data["brands"]]
    keyword_rows = [(kind, category, word, (rank + 1) * 10)
                    for kind, name in _KEYWORD_FILES
                    for rank, (category, words) in enumerate(data_files.keyword_table(name).items())
                    for word in words]
    return brand_rows, keyword_rows


def _known(api: AiApi) -> tuple[set, set]:
    """(thương hiệu, (loại, từ khóa)) đã có dòng trong AI Service, kể cả dòng đã tắt hay đã chuyển sang nhóm khác: đó là quyết định của AI Service, không phải thiếu."""
    knowledge = api.knowledge(include_inactive=True)
    # không xét nhóm: từ đã được chuyển nhóm (migration, người duyệt) thì không thêm lại ở nhóm cũ
    return {b["brand_key"] for b in knowledge["brands"]}, {(k["kind"], k["keyword"]) for k in knowledge["keywords"]}


def missing(api: AiApi) -> dict:
    """Mục có trong data/*.json mà AI Service chưa có (xem _known). Đây là phần seed() sẽ thêm."""
    brand_rows, keyword_rows = _defaults()
    have_brands, have_keywords = _known(api)
    return {"brands": [key for key, *_ in brand_rows if key not in have_brands],
            "keywords": [(kind, category, word) for kind, category, word, _ in keyword_rows if (kind, word) not in have_keywords]}


def seed(api: AiApi) -> dict:
    """Thêm những mục data/*.json còn thiếu (chạy lại an toàn: dòng đã có, đã sửa, đã tắt hay từ khóa đã chuyển nhóm đều giữ nguyên, không bao giờ ghi đè). Trả về số dòng mới."""
    brand_rows, keyword_rows = _defaults()
    have_brands, have_keywords = _known(api)
    return api.seed_knowledge(
        [{"brand_key": key, "category_slug": category, "display_name": display, "whole_word": whole}
         for key, category, display, whole in brand_rows if key not in have_brands],
        [{"kind": kind, "category_slug": category, "keyword": word, "category_priority": priority}
         for kind, category, word, priority in keyword_rows if (kind, word) not in have_keywords],
    )


def load(api: AiApi) -> dict | None:
    """Đọc kiến thức đang bật từ AI Service; chưa có gì thì None."""
    knowledge = api.knowledge()
    if not knowledge["brands"] and not knowledge["keywords"]:
        return None
    by_kind: dict[str, dict[str, list[str]]] = {"merchant": {}, "product": {}}
    for row in knowledge["keywords"]:  # đã sắp theo category_priority: giữ đúng thứ tự ưu tiên giữa các nhóm
        by_kind.setdefault(row["kind"], {}).setdefault(row["category_slug"], []).append(row["keyword"])
    return {"brands": {b["brand_key"]: (b["category_slug"], b["display_name"], bool(b["whole_word"])) for b in knowledge["brands"]},
            **{kind: {c: tuple(w) for c, w in table.items()} for kind, table in by_kind.items()}}


def apply(knowledge: dict) -> None:
    """Thay nội dung các danh sách trong code bằng dữ liệu từ AI Service (tại chỗ). Phần nào AI Service không có thì giữ bản trong code."""
    if knowledge["brands"]:
        for target in (brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS):
            target.clear()
        for key, (category, display, whole) in knowledge["brands"].items():
            brands.BRAND_CATEGORIES[key], brands.BRAND_DISPLAY[key] = category, display
            if whole:
                brands.WHOLE_WORD_BRANDS.add(key)
    for new, table in ((knowledge["merchant"], extract._CATEGORY_KEYWORDS), (knowledge["product"], extract._PRODUCT_KEYWORDS)):
        if new:
            table.clear()
            table.update(new)


def reload(api: AiApi) -> bool:
    knowledge = load(api)
    if knowledge is None:
        return False
    apply(knowledge)
    return True


def open_knowledge():
    """(nơi ghi bill lạ, hàm reload kiến thức). Có API: nạp thương hiệu/từ khóa ngay khi khởi động; không có: giữ bản trong code, không ghi gì."""
    api = api_from_env()
    if not api:
        return NoUnknownScans(), lambda: False
    try:
        reload(api)
    except Exception:  # noqa: BLE001 - AI Service chưa lên hoặc chưa migrate không được làm API chết lúc khởi động
        logging.getLogger(__name__).warning("Chưa nạp được kiến thức từ AI Service: dùng bản trong code", exc_info=True)
    return ApiUnknownScans(api, os.environ.get("FINMATE_USER_HASH", "local")), lambda: reload(api)


class ApiUnknownScans:
    """Ghi bill lạ vào hàng đợi duyệt của AI Service để người duyệt thêm vào bảng thương hiệu."""

    def __init__(self, api: AiApi, user_hash: str):
        self._api, self._user = api, user_hash

    def record(self, merchant: str | None, lines: list[str], total: int | None, predicted_category: str | None,
               predicted_source: str | None, confidence: float | None) -> None:
        key = _key(merchant or "")
        content_hash = hashlib.sha256("\n".join([key, str(total), *lines]).encode("utf-8")).hexdigest()
        self._api.record_unknown_scan({
            "user_id_hash": who(self._user), "content_hash": content_hash, "merchant_read": merchant, "merchant_key": key, "predicted_category": predicted_category,
            "predicted_source": predicted_source, "confidence": confidence, "total_amount": total, "lines": lines,
        })

    def mark_labeled(self, merchant: str | None) -> None:
        """Người dùng đã chốt danh mục cho cửa hàng này: các bill lạ cùng tên không cần xem xét nữa."""
        key = _key(merchant or "")
        if key:
            self._api.label_unknown_scans(key)


class NoUnknownScans:
    """Không có API thì không ghi gì (bản chạy bằng file như cũ)."""

    def record(self, *args, **kwargs) -> None:
        pass

    def mark_labeled(self, merchant) -> None:
        pass


__all__ = ["AiApiError", "ApiUnknownScans", "NoUnknownScans", "apply", "load", "missing", "open_knowledge", "reload", "seed"]
