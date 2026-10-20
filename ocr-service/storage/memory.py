"""Nhớ danh mục và tên đẹp người dùng đã chốt cho từng cửa hàng (không lưu ảnh hay số tiền)."""
import json
import os
from difflib import SequenceMatcher
from pathlib import Path

from parsing.extract import CATEGORIES, _alnum, _looks_like_code_or_status, _norm, _within_one_edit

_FUZZY_MIN_LEN = 5
_FUZZY_RATIO = 0.85
_SCAN_MIN_LEN = 6        # tên ngắn hơn (đã bỏ dấu, dấu cách) không dò trong mọi dòng, tránh khớp nhầm
_SCAN_EXTRA_CHARS = 3    # dòng chứa tên thì chỉ được dài hơn tên tối đa chừng này ký tự


def _key(merchant: str) -> str:
    return " ".join(_norm(merchant).split())


def _entry(value) -> dict:
    """Mục cũ chỉ là chuỗi danh mục; mục mới là {"category": ..., "name": ...}."""
    return {"category": value} if isinstance(value, str) else dict(value)


class MerchantMemory:
    def __init__(self, path: Path):
        self._path = Path(path)

    def _load(self) -> dict:
        try:
            data = json.loads(self._path.read_text(encoding="utf-8-sig"))  # chịu được BOM
            return data if isinstance(data, dict) else {}
        except (OSError, ValueError):
            return {}  # thiếu file hoặc file hỏng: coi như chưa nhớ gì

    def _find(self, merchant: str | None) -> dict | None:
        if not merchant:
            return None
        data, key = self._load(), _key(merchant)
        if key in data:
            return _entry(data[key])
        if len(key) < _FUZZY_MIN_LEN:
            return None  # khóa ngắn chỉ khớp chính xác để khỏi nhận nhầm
        # OCR đọc logo mỗi lần một kiểu ("FPTAvtail" / "FPTRvtail"): chấp nhận tên gần giống.
        best_key = max(
            (k for k in data if len(k) >= _FUZZY_MIN_LEN),
            key=lambda k: SequenceMatcher(None, key, k).ratio(),
            default=None,
        )
        if best_key and SequenceMatcher(None, key, best_key).ratio() >= _FUZZY_RATIO:
            return _entry(data[best_key])
        return None

    def get(self, merchant: str | None) -> str | None:
        entry = self._find(merchant)
        return entry["category"] if entry else None

    def display_name(self, merchant: str | None) -> str | None:
        """Tên đẹp người dùng đã chốt (có dấu); None nếu chưa có."""
        entry = self._find(merchant)
        return entry.get("name") if entry else None

    def find_in_lines(self, lines: list[str]) -> str | None:
        """Dòng nào trong hóa đơn là tên cửa hàng đã biết (cả tên đẹp lẫn tên OCR thô đã lưu), ở bất kỳ vị trí."""
        keys = [sk for sk in (_alnum(k) for k in self._load()) if len(sk) >= _SCAN_MIN_LEN]
        # bỏ dòng người mua / trường thông tin / địa chỉ (tên cửa hàng của chính bạn hay xuất hiện ở dòng người mua)
        candidates = [(line, _alnum(_norm(line))) for line in lines if not _looks_like_code_or_status(line)]
        for line, squeezed in candidates:                 # lượt 1: khớp chính xác hoặc lệch tối đa 1 ký tự
            if any(squeezed == sk or _within_one_edit(squeezed, sk) for sk in keys):
                return line
        for line, squeezed in candidates:                 # lượt 2: dòng chỉ chứa tên, dư không quá vài ký tự
            if any(sk in squeezed and len(squeezed) <= len(sk) + _SCAN_EXTRA_CHARS for sk in keys):
                return line
        return None

    def set(self, merchant: str, category: str, name: str | None = None) -> None:
        key = _key(merchant or "")
        if not key:
            raise ValueError("Tên cửa hàng trống")
        if category not in CATEGORIES:
            raise ValueError(f"Danh mục không hợp lệ: {category}")
        data = self._load()
        entry = _entry(data.get(key, {}))
        entry["category"] = category
        if name:
            entry["name"] = name
        self._save(key, entry, data)

    def _save(self, key: str, entry: dict, data: dict) -> None:
        data[key] = entry
        tmp = self._path.with_suffix(".tmp")
        tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        os.replace(tmp, self._path)  # ghi nguyên tử, tránh file dở dang
