"""Ngày của khoản trong câu chat: "hôm qua", "hôm kia", "sáng/trưa/chiều/tối nay|qua", "ngày 5/10", "ngày 5 tháng 10".

Chỉ nhận ngày KHÔNG ở tương lai; chưa tới hoặc không hợp lệ thì coi như không có (backend dùng giờ hiện tại). Ngày số bắt buộc có chữ
"ngày"/"hôm" đứng trước: "giảm 20/10" là khuyến mãi, không phải ngày. Ngày không rõ giờ lấy 12:00 giờ Việt Nam để múi giờ không đẩy sang ngày khác.
"""
from __future__ import annotations

import re
import unicodedata
from datetime import datetime, timedelta, timezone
from typing import NamedTuple

from app.services.amount_normalizer import normalize

VN = timezone(timedelta(hours=7))  # Việt Nam không có giờ mùa hè
_DAYPART_HOUR = {"sang": 8, "trua": 12, "chieu": 17, "toi": 19}
_RELATIVE = re.compile(r"(?<![a-z])(?:(?P<part>sang|trua|chieu|toi) (?P<which>nay|qua)|hom (?P<day>nay|qua|kia))(?![a-z])")
_NUMERIC = re.compile(r"(?<![a-z])(?:ngay|hom) (?P<d>\d{1,2})(?:/| thang |-)(?P<m>\d{1,2})(?![\d/])")


class When(NamedTuple):
    value: datetime
    start: int  # vị trí trong câu đã chuẩn hóa (cùng độ dài với câu gốc NFC), như AmountMatch
    end: int


def _noon(day: datetime) -> datetime:
    return day.replace(hour=12, minute=0, second=0, microsecond=0)


def parse_when(text: str, now: datetime) -> When | None:
    norm = normalize(text)
    found = _RELATIVE.search(norm)
    if found and found.group("part") == "toi" and "tối" not in unicodedata.normalize("NFC", text).lower()[found.start() : found.end()]:
        found = None  # "tôi qua quán ăn" = tôi (đại từ) + qua, bỏ dấu trùng "tối qua": chỉ nhận khi có dấu đúng
    if found:
        today = now.astimezone(VN)
        if found.group("part"):
            back = 1 if found.group("which") == "qua" else 0
            value = (today - timedelta(days=back)).replace(hour=_DAYPART_HOUR[found.group("part")], minute=0, second=0, microsecond=0)
        else:
            back = {"nay": 0, "qua": 1, "kia": 2}[found.group("day")]
            value = today if back == 0 else _noon(today - timedelta(days=back))
        return When(value, found.start(), found.end()) if value <= now else None
    found = _NUMERIC.search(norm)
    if found:
        try:
            value = datetime(now.year, int(found.group("m")), int(found.group("d")), 12, tzinfo=VN)
        except ValueError:
            return None
        return When(value, found.start(), found.end()) if value <= now else None
    return None


def remove_when(text: str, when: When) -> str:
    """Câu gốc đã bỏ cụm ngày (và khoảng trắng thừa)."""
    nfc = unicodedata.normalize("NFC", text)
    return re.sub(r"\s+", " ", nfc[: when.start] + " " + nfc[when.end :]).strip(" ,.-")
