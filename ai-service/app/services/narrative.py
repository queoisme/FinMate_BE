"""Tách phần 'nội dung' (sau nhãn như ``ND:``) khỏi thông báo ngân hàng. Nhãn sống trong DB (tài liệu ``narrative_labels``), tệp JSON chỉ là seed."""
from __future__ import annotations

import json
import pathlib
import re

from app.services.vi_text import fold

_PATH = pathlib.Path(__file__).resolve().parent.parent / "data" / "narrative_labels.json"
MAX_DESCRIPTION_CHARS = 255
_STRIP = " :.-|\t\r\n"
# Nội dung kết thúc khi gặp trường kế tiếp: sau một ranh giới mệnh đề là cụm chữ ngắn rồi dấu hai chấm ("\nMã giao dịch:", ". Số dư cuối:"). Cụm phải mở đầu bằng chữ để "12:30" không bị cắt.
_NEXT_FIELD = re.compile(r"[.|;\n]\s*[^\W\d_][^.|;\n:]{1,24}:")


def default_data() -> dict[str, list[str]]:
    return json.loads(_PATH.read_text(encoding="utf-8"))


def build(content: dict) -> tuple[str, ...]:
    """Nhãn đã fold, không trùng, dài trước (nhãn dài thắng nhãn ngắn là tiền tố của nó)."""
    return tuple(sorted({fold(label) for label in content["labels"]}, key=lambda label: (-len(label), label)))


def _pattern(labels: tuple[str, ...]) -> re.Pattern[str] | None:
    if not labels:
        return None
    return re.compile(r"(?:^|[.|;\n])\s*(?:" + "|".join(re.escape(label) for label in labels) + r")(?=[:\s]|$)")


_data = default_data()
_regex = _pattern(build(_data))


def apply(content: dict) -> None:
    """Đổi nhãn đang chạy trong tiến trình; dựng xong rồi mới gán."""
    global _data, _regex
    regex = _pattern(build(content))
    _data, _regex = content, regex


def current_data() -> dict[str, list[str]]:
    return _data


def extract_description(text: str) -> str | None:
    """Phần sau nhãn đứng đầu mệnh đề (lần xuất hiện đầu tiên); None nếu không có nhãn hợp lệ hoặc phần sau rỗng.

    Truyền thân GỐC chưa gộp khoảng trắng: nhiều ngân hàng đặt mỗi trường một dòng, và xuống dòng là ranh giới mệnh đề duy nhất ở đó.
    """
    regex = _regex
    if regex is None:
        return None
    folded, origin = [], []
    for index, char in enumerate(text):  # fold từng ký tự: fold cả chuỗi có thể đổi độ dài nên vị trí sẽ lệch
        piece = fold(char)
        folded.append(piece)
        origin.extend([index] * len(piece))
    flat = "".join(folded)
    match = regex.search(flat)
    if match is None:
        return None
    start = origin[match.end()] if match.end() < len(origin) else len(text)
    rest = text[start:]
    if (next_field := _NEXT_FIELD.search(rest)) is not None:
        rest = rest[: next_field.start()]
    tail = " ".join(rest.split())  # thân gốc còn xuống dòng giữa các trường; nội dung dài cũng bị ngắt dòng
    return tail.strip(_STRIP)[:MAX_DESCRIPTION_CHARS].strip(_STRIP) or None
