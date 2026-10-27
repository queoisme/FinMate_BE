"""Ngày giao dịch trên hóa đơn từ các dòng chữ OCR: "11/04/2026 13:16", "2026-10-09 13:38", "Ngày 06 tháng 10 năm 2026", và dạng bị OCR dính chữ ("Ngay11-04-202613:16").

Trả về giờ Việt Nam (+07:00). Không có giờ thì đặt 12:00 để ngày không lệch khi đổi sang UTC.
Hóa đơn hay in nhiều ngày: kỳ tính phí ("từ 01/05 đến 31/05"), hạn thanh toán ("trước ngày 20"), ngày in. Luật chọn:
bỏ dòng có nhiều ngày hoặc nhãn kỳ/hạn, bỏ ngày ở tương lai (OCR đọc sai hay ra năm 2062), rồi lấy ngày MUỘN nhất còn lại.
ponytail: chưa đọc ngày chỉ có tháng ("10/2026") hay ngày tháng không có năm.
"""
import re
import unicodedata
from datetime import datetime, timedelta, timezone
from classification import data_files

VN = timezone(timedelta(hours=7))
_MIN_YEAR = data_files.threshold("parsing.dates._MIN_YEAR")
_FUTURE_SLACK = timedelta(days=1)

# dd/mm/yyyy (hoặc dd-mm-yy, dd.mm.yyyy); năm 4 chữ số được phép dính liền với giờ: "11-04-202613:16"
_DMY = re.compile(r"(?<!\d)(\d{1,2})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(?:(\d{4})(?=\d{1,2}:\d{2}|\D|$)|(\d{2})(?!\d))")
# yyyy-mm-dd (hoặc yyyy/mm/dd, yyyy.mm.dd) của máy POS; ngày 2 chữ số được phép dính liền với giờ: "2026-10-0913:38"
_YMD = re.compile(r"(?<!\d)(\d{4})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(?:(\d{2})(?=\d{1,2}:\d{2})|(\d{1,2})(?!\d))")
_TIME = re.compile(r"\s*(\d{1,2}):(\d{2})")
_BARE_TIME = re.compile(r"^\s*(\d{1,2}):(\d{2})(?::\d{2})?\s*$")  # dòng chỉ có giờ ("12:31:13"): OCR hay tách giờ khỏi ngày thành hai dòng
_WORDS = re.compile(r"ngay\s*(\d{1,2})\s*thang\s*(\d{1,2})\s*nam\s*(\d{4})")
_PERIOD_OR_DUE = re.compile(r"(?:^|\s)(?:tu ngay|den ngay|ky tinh phi|ky thanh toan|han thanh toan|han cuoi|truoc ngay|het han)(?:\s|$)|\btu\b.*\bden\b")


def _fold(text: str) -> str:
    text = unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D"))
    return "".join(c for c in text if unicodedata.category(c) != "Mn").lower()


def _build(day: int, month: int, year: int, hour: int | None, minute: int | None) -> datetime | None:
    if year < 100:
        year += 2000
    try:
        return datetime(year, month, day, 12 if hour is None else hour, 0 if minute is None else minute, tzinfo=VN)
    except ValueError:
        return None  # ngày không tồn tại (31/02) hoặc giờ ngoài 0-23: OCR đọc sai


def _candidates(line: str) -> list[datetime]:
    found = []
    folded = _fold(line)
    for m in _DMY.finditer(line):
        time = _TIME.match(line, m.end())
        year = int(m.group(3) or m.group(4))
        built = _build(int(m.group(1)), int(m.group(2)), year, int(time.group(1)) if time else None, int(time.group(2)) if time else None)
        if built:
            found.append(built)
    for m in _YMD.finditer(line):
        time = _TIME.match(line, m.end())
        built = _build(int(m.group(3) or m.group(4)), int(m.group(2)), int(m.group(1)), int(time.group(1)) if time else None, int(time.group(2)) if time else None)
        if built:
            found.append(built)
    for m in _WORDS.finditer(folded):
        built = _build(int(m.group(1)), int(m.group(2)), int(m.group(3)), None, None)
        if built:
            found.append(built)
    return found


def find_date(lines: list[str], now: datetime | None = None) -> datetime | None:
    now = (now or datetime.now(VN)).astimezone(VN)
    best = None
    for i, line in enumerate(lines):
        found = _candidates(line)
        if len(found) > 1 or _PERIOD_OR_DUE.search(_fold(line)):
            continue  # khoảng ngày hoặc hạn/kỳ tính phí, không phải ngày giao dịch
        if found and not re.search(r"\d{1,2}:\d{2}", line):  # ngày không kèm giờ cùng dòng: lấy giờ ở dòng sát bên nếu dòng đó chỉ có giờ
            for j in (i - 1, i + 1):
                m = _BARE_TIME.match(lines[j]) if 0 <= j < len(lines) else None
                if m and int(m.group(1)) <= 23 and int(m.group(2)) <= 59:
                    found = [v.replace(hour=int(m.group(1)), minute=int(m.group(2))) for v in found]
                    break
        for value in found:
            if _MIN_YEAR <= value.year and value <= now + _FUTURE_SLACK and (best is None or value > best):
                best = value
    return best
