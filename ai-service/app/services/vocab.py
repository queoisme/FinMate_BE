"""Từ vựng cho câu chat/giọng nói ngắn ("đi ăn 200k", "mua sắm 1 triệu"): bổ sung cho bảng từ khóa của bill, vì chat dùng từ chung chung
("ăn", "nhậu", "chợ", "áo") mà bill hiếm khi có. Đã bỏ dấu, chữ thường; khớp nguyên từ, từ dài nhất thắng.
Hai khóa ``education`` và ``family`` KHÔNG nằm trong 9 nhóm của bill mà là slug backend dùng thẳng (route chat nhận cả hai kiểu).

Bản mặc định ở ``dictionaries/*.json`` (mỗi nhóm một tệp: kind, label, keywords). Admin sửa trên trang quản trị (tài liệu ``chat_dictionary`` trong
``config_documents``): bản được kích hoạt thay bản mặc định NGAY trong tiến trình (``apply``), không phải khởi động lại. Tài liệu là một đối tượng
``{tên nhóm: {kind, label, keywords}}`` (tên nhóm = tên tệp). Nơi giữ giá trị dẫn xuất từ từ vựng (``transaction_parser``, ``category_resolver``) gắn lại qua ``subscribe``."""
from __future__ import annotations

import json
from pathlib import Path

DICTIONARY_DIR = Path(__file__).resolve().parents[2] / "dictionaries"
KINDS = ("expense", "income")


def default_data(directory: Path = DICTIONARY_DIR) -> dict[str, dict]:
    """Tài liệu mặc định: gộp các tệp ``*.json`` thành ``{tên nhóm: {kind, label, keywords}}``. Tệp hỏng thì báo lỗi to:
    thiếu từ vựng một cách im lặng làm câu chat bị xếp sai danh mục mà không có dấu hiệu nào."""
    data: dict[str, dict] = {}
    for path in sorted(directory.glob("*.json")):
        try:
            data[path.stem] = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            raise ValueError(f"Từ điển {path.name} không đọc được: {error}") from error
    return data


def build(data: dict, source: str = "dictionaries") -> tuple[dict[str, tuple[str, ...]], dict[str, str], dict[str, str]]:
    """(từ khóa theo nhóm, nhãn danh mục chi tiêu, nhãn loại thu nhập) từ một tài liệu. Sai thì ValueError nói rõ nhóm nào sai ở đâu."""
    keywords: dict[str, tuple[str, ...]] = {}
    categories: dict[str, str] = {}
    incomes: dict[str, str] = {}
    for name, entry in data.items():
        try:
            kind, label, words = entry["kind"], entry["label"], tuple(entry["keywords"]) + tuple(entry.get("weak_keywords", ()))  # từ yếu vẫn là từ khóa của nhóm
        except (KeyError, TypeError) as error:
            raise ValueError(f"Từ điển {source}/{name} không đọc được: {error}") from error
        if kind not in KINDS:
            raise ValueError(f"Từ điển {source}/{name}: kind phải là expense hoặc income")
        if words:
            keywords[name] = words
        (categories if kind == "expense" else incomes)[name] = label
    return keywords, categories, incomes


def weak_of(data: dict) -> frozenset[str]:
    """Từ khóa YẾU của mọi nhóm (trường tùy chọn ``weak_keywords``): vẫn là từ khóa của nhóm, nhưng chỉ quyết danh mục khi câu không có từ khóa thường nào khớp
    ("cho con": tiền đến tay ai, không nói mua gì)."""
    return frozenset(word for entry in data.values() for word in entry.get("weak_keywords", ()))


def load_dictionaries(directory: Path) -> tuple[dict[str, tuple[str, ...]], dict[str, str], dict[str, str]]:
    """Dựng ba bảng từ thư mục ``*.json`` (bản mặc định; test dùng thư mục tạm)."""
    return build(default_data(directory), str(directory))


CHAT_KEYWORDS, CATEGORY_LABELS, INCOME_LABELS = load_dictionaries(DICTIONARY_DIR)

_current: dict[str, dict] = default_data()
WEAK_KEYWORDS: frozenset[str] = weak_of(_current)
_listeners: list = []


def current_data() -> dict[str, dict]:
    """Tài liệu của bản đang chạy (mặc định, hoặc bản admin vừa kích hoạt): để xem trước xong thì trả lại đúng bản này."""
    return _current


def subscribe(callback) -> None:
    """Đăng ký hàm (không tham số) được gọi sau mỗi lần ``apply``: nơi giữ giá trị dẫn xuất dựng lại từ ``vocab.*`` mới."""
    _listeners.append(callback)


def apply(data: dict) -> None:
    """Đổi từ vựng đang chạy trong tiến trình này. Dựng xong cả ba bảng rồi mới gán (sai giữa chừng thì không đổi gì), sau đó báo cho các bên đăng ký.
    Nơi khác phải đọc ``vocab.CHAT_KEYWORDS``... qua module (không ``from ... import``), nếu không sẽ giữ bản cũ."""
    global CHAT_KEYWORDS, CATEGORY_LABELS, INCOME_LABELS, WEAK_KEYWORDS, _current
    CHAT_KEYWORDS, CATEGORY_LABELS, INCOME_LABELS = build(data, "DB")
    WEAK_KEYWORDS = weak_of(data)
    _current = data
    for callback in _listeners:
        callback()
