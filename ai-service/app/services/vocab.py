"""Từ vựng cho câu chat/giọng nói ngắn ("đi ăn 200k", "mua sắm 1 triệu"): bổ sung cho bảng từ khóa của bill, vì chat dùng từ chung chung
("ăn", "nhậu", "chợ", "áo") mà bill hiếm khi có. Đã bỏ dấu, chữ thường; khớp nguyên từ, từ dài nhất thắng.
Hai khóa ``education`` và ``family`` KHÔNG nằm trong 9 nhóm của bill mà là slug backend dùng thẳng (route chat nhận cả hai kiểu).
Từ vựng nằm ở ``dictionaries/*.json`` (mỗi nhóm một tệp: kind, label, keywords): sửa tệp rồi khởi động lại ai-service, không phải sửa code.
ponytail: nạp một lần lúc import; muốn sửa không cần khởi động lại thì chuyển vào DB của AI (cần migration, chờ duyệt)."""

import json
from pathlib import Path

DICTIONARY_DIR = Path(__file__).resolve().parents[2] / "dictionaries"


def load_dictionaries(directory: Path) -> tuple[dict[str, tuple[str, ...]], dict[str, str], dict[str, str]]:
    """(từ khóa theo nhóm, nhãn danh mục chi tiêu, nhãn loại thu nhập) từ các tệp ``*.json`` trong [directory]. Tệp hỏng thì báo lỗi to:
    thiếu từ vựng một cách im lặng làm câu chat bị xếp sai danh mục mà không có dấu hiệu nào."""
    keywords: dict[str, tuple[str, ...]] = {}
    categories: dict[str, str] = {}
    incomes: dict[str, str] = {}
    for path in sorted(directory.glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            kind, label, words = data["kind"], data["label"], tuple(data["keywords"])
        except (OSError, ValueError, KeyError, TypeError) as error:
            raise ValueError(f"Từ điển {path.name} không đọc được: {error}") from error
        if kind not in ("expense", "income"):
            raise ValueError(f"Từ điển {path.name}: kind phải là expense hoặc income")
        if words:
            keywords[path.stem] = words
        (categories if kind == "expense" else incomes)[path.stem] = label
    return keywords, categories, incomes


CHAT_KEYWORDS, CATEGORY_LABELS, INCOME_LABELS = load_dictionaries(DICTIONARY_DIR)
