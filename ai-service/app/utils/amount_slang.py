"""Từ lóng chỉ tiền ("hai củ", "một lít"), đọc từ ``app/data/amount_slang.json``.

Giá trị nằm trong file dữ liệu để người vận hành tự chỉnh, vì cùng một từ đổi nghĩa theo vùng
và theo người nói. Điều duy nhất đứng ngoài file là việc "đã có từ lóng thì coi như đơn vị":
mỗi từ hoạt động y hệt "nghìn"/"triệu" ở cả đường chữ số lẫn đường chữ.

Nhiều từ lóng còn là từ thường ("lít" xăng, "củ" cải), nên mỗi từ có hai lối thoát khai báo
trong file: từ đứng ngay sau, và từ khoá trong câu.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path

_DATA = Path(__file__).resolve().parent.parent / "data" / "amount_slang.json"


@dataclass(frozen=True)
class SlangUnit:
    word: str
    vnd: int
    not_followed_by: tuple[str, ...]
    not_with_context: tuple[str, ...]
    # Mẫu regex khớp ở ĐẦU phần câu đứng sau từ lóng: cho trường hợp một từ đơn không đủ ("củ từ" là khoai môn, nhưng "1 củ từ ví" là 1 triệu rút từ ví).
    not_followed_by_pattern: tuple[re.Pattern[str], ...] = ()


def _load() -> dict[str, SlangUnit]:
    units: dict[str, SlangUnit] = {}
    for entry in json.loads(_DATA.read_text(encoding="utf-8"))["units"]:
        for word in entry["words"]:
            units[word.lower()] = SlangUnit(
                word=word.lower(),
                vnd=int(entry["vnd"]),
                not_followed_by=tuple(w.lower() for w in entry.get("not_followed_by", [])),
                not_with_context=tuple(w.lower() for w in entry.get("not_with_context", [])),
                not_followed_by_pattern=tuple(re.compile(p) for p in entry.get("not_followed_by_pattern", [])),
            )
    return units


SLANG: dict[str, SlangUnit] = _load()


def is_money(word: str, text: str, end: int) -> bool:
    """Từ lóng ``word`` kết thúc ở vị trí ``end`` của ``text`` có đang nghĩa là tiền không."""
    unit = SLANG[word.lower()]
    lowered = text.lower()
    if any(context in lowered for context in unit.not_with_context):
        return False
    if any(pattern.match(lowered[end:].lstrip()) for pattern in unit.not_followed_by_pattern):
        return False
    following = lowered[end:].split()
    return not (following and following[0].strip(".,;:!?") in unit.not_followed_by)
