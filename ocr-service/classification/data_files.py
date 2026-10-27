"""Đọc bảng dữ liệu mặc định trong thư mục data/ ở gốc dịch vụ (đường dẫn tính theo vị trí file, không theo thư mục chạy)."""
import json
import re
from pathlib import Path

DATA_DIR = Path(__file__).resolve().parent.parent / "data"


def load(name: str):
    """Đọc data/<name>. Thiếu file thì báo lỗi ngay: chạy với bảng rỗng sẽ phân loại sai mà không ai biết."""
    with open(DATA_DIR / name, encoding="utf-8") as f:
        return json.load(f)


def keyword_table(name: str) -> dict[str, tuple[str, ...]]:
    """Bảng nhóm -> các từ khóa. Thứ tự nhóm trong file là thứ tự ưu tiên; giá trị là tuple như bản viết cứng trước đây."""
    return {category: tuple(words) for category, words in load(name).items()}


def shared_words(*roles: str) -> tuple[str, ...]:
    """Từ của một hay nhiều VAI TRÒ trong data/shared_words.json (từ vựng hóa đơn chung với ai-service và backend), theo thứ tự trong file."""
    data = load("shared_words.json")
    return tuple(word for role in roles for word in data[role]["words"])


def shared_alternation(*roles: str, extra_regex: tuple[str, ...] = ()) -> str:
    """Chuỗi ``a|b|c`` (đã escape) cho regex từ các vai trò ``shared_words``; ``extra_regex`` là tên vai trò chứa mẫu regex thô (``tax_ocr_misread``)."""
    data = load("shared_words.json")
    return "|".join([re.escape(word) for word in shared_words(*roles)] + [pattern for role in extra_regex for pattern in data[role]["regex"]])


_VOCAB: dict | None = None
_THRESHOLDS: dict | None = None
_SCREENS: dict | None = None


def build_table(key: str, entry: dict):
    """Chuyển một mục của parsing_vocab (``{type, doc, value}``) về đúng KIỂU đã viết cứng trước đây (tuple, tuple lồng, set, dict, list) để mã dùng nó không đổi."""
    value, kind = entry["value"], entry["type"]
    if kind == "tuple":
        return tuple(value)
    if kind == "tuple2":
        return tuple(tuple(inner) for inner in value)
    if kind == "set":
        return set(value)
    if kind == "list":
        return list(value)
    if kind == "dict":
        return dict(value)
    raise ValueError(f"parsing_vocab: {key} có kiểu lạ {kind!r}")


def vocab_document() -> dict:
    """Tài liệu từ vựng đang dùng: bản admin đã kích hoạt trong DB (xem ``override``) hoặc data/parsing_vocab.json."""
    global _VOCAB
    if _VOCAB is None:
        _VOCAB = load("parsing_vocab.json")
    return _VOCAB


def thresholds_document() -> dict:
    global _THRESHOLDS
    if _THRESHOLDS is None:
        _THRESHOLDS = load("thresholds.json")
    return _THRESHOLDS


def screens_document() -> dict:
    """Chữ/hằng số theo giao diện từng app (``parsing/app_screens.py``): bản admin đã kích hoạt trong DB (xem ``override``) hoặc data/app_screens.json."""
    global _SCREENS
    if _SCREENS is None:
        _SCREENS = load("app_screens.json")
    return _SCREENS


def override(vocab: dict | None, thresholds: dict | None, screens: dict | None = None) -> None:
    """Đặt tài liệu đang dùng (từ DB). ``None`` = quay về tệp trong data/. Chỉ đổi dữ liệu; gắn lại vào các mô-đun là việc của storage/config_api.py."""
    global _VOCAB, _THRESHOLDS, _SCREENS
    _VOCAB = vocab if vocab is not None else load("parsing_vocab.json")
    _THRESHOLDS = thresholds if thresholds is not None else load("thresholds.json")
    _SCREENS = screens if screens is not None else load("app_screens.json")


def table(key: str):
    """Bảng từ vựng của một mô-đun (khóa "parsing.extract._RECEIPT_VOCAB"), trả đúng kiểu như bản viết cứng trước đây."""
    return build_table(key, vocab_document()[key])


def threshold(key: str):
    """Ngưỡng số. Mỗi mục ghi số mẫu đã dùng để chỉnh ("samples") và nguồn: ngưỡng chỉnh theo vài mẫu chưa đáng tin cho ảnh lạ."""
    return thresholds_document()[key]["value"]
