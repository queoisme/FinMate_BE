"""Tách phần 'nội dung' (sau nhãn như ``ND:``) khỏi thông báo ngân hàng. Nhãn sống trong DB (tài liệu ``narrative_labels``), tệp JSON chỉ là seed."""
from __future__ import annotations

import json
import pathlib
import re

from app.services.vi_text import fold

_PATH = pathlib.Path(__file__).resolve().parent.parent / "data" / "narrative_labels.json"
MAX_DESCRIPTION_CHARS = 255
_STRIP = " :.-|\t\r\n"


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
    """Phần sau nhãn đứng đầu mệnh đề (lần xuất hiện đầu tiên); None nếu không có nhãn hợp lệ hoặc phần sau rỗng."""
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
    return text[start:].strip(_STRIP)[:MAX_DESCRIPTION_CHARS].strip(_STRIP) or None
