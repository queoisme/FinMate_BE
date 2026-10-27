"""Đọc số tiền trong câu chat/giọng nói tiếng Việt: "200k", "45 cành", "3 củ 8", "1 lít 2", "3 lít rưỡi", "2 triệu rưỡi", "50.000đ", "một triệu"...

Quy ước tiếng lóng: k/nghìn/ngàn/cành = 1.000; tr/triệu = 1.000.000; tỷ = 1.000.000.000. Từ lóng (củ, lít, chai, tỏi...) lấy từ
``app/data/amount_slang.json``, dùng chung với bộ đọc voice ("1 lít 2" = 120.000, "3 lít rưỡi" = 350.000);
chữ số ngay sau đơn vị là phần thập phân của đơn vị ("3 củ 8" = 3,8 triệu, "1tr2" = 1,2 triệu); "rưỡi" = +0,5 đơn vị.
Nhiều số trong câu ("mua 2 lít sữa 50k") thì lấy số xuất hiện CUỐI: người ta thường nói giá ở cuối câu.

Số đọc bằng CHỮ có dấu ("hai củ", "ba tỷ", "hai chục ngàn") giao cho ``app.utils.number_words`` — cùng một bộ đọc với voice. Chữ không dấu
đi đường cũ ở ``vi_text`` (chịu được OCR hỏng), và từ lóng chỉ nhận khi viết đúng dấu: bỏ dấu thì "tỏi"/"tôi"/"tối" là một.
"""
import functools
import re
import unicodedata
from typing import NamedTuple

from app.services.vi_text import _DIGITS, _NUMBER_WORDS, _WORD_ALIAS, fold, words_to_amount
from app.utils.amount_slang import SLANG, is_money
from app.utils.number_words import find_number_words

UNITS = {"k": 1_000, "nghin": 1_000, "ngan": 1_000, "canh": 1_000, "tr": 1_000_000, "trieu": 1_000_000, "ty": 1_000_000_000}
# Khóa là chữ đã bỏ dấu vì câu chat được khớp sau khi bỏ dấu; _SLANG_WORD nhớ lại dạng có dấu để kiểm tra.
_SLANG_WORD = {fold(word): word for word in SLANG}
UNITS.update({fold(word): unit.vnd for word, unit in SLANG.items()})
_UNACCENTED_OK = {"củ", "lít"}  # gõ không dấu vẫn quen ("2 cu", "3 lit"): giữ hành vi cũ cho hai từ này
_NUM = r"\d+(?:[.,]\d+)?"
_UNIT_ALTERNATION = "|".join(sorted(UNITS, key=len, reverse=True))  # dài trước ngắn để "tr" không nuốt "trieu"
_MULTIPLIER = {"chuc": 10, "tram": 100}  # "2 chục ngàn" = 20 nghìn, "3 trăm k" = 300k: số, rồi chục/trăm, rồi đơn vị
_SLANG = re.compile(rf"(?<![\w.,])(?P<n>{_NUM})(?:\s*(?P<m>chuc|tram)(?![a-z]))?\s*(?P<u>{_UNIT_ALTERNATION})(?![a-z])")
_TENS_ONES = {"mot": 1, "hai": 2, "ba": 3, "bon": 4, "tu": 4, "nam": 5, "lam": 5, "sau": 6, "bay": 7, "tam": 8, "chin": 9}
# STT hay chép "hai mươi lăm" thành "2 mươi lăm": chữ số đứng trước "mươi" là hàng chục. Chỉ "mươi" (hai mươi...), "mười" là 10 nên không đi đường này.
_DIGIT_TENS = re.compile(rf"(?<![\w.,])(?P<d>[2-9])\s*muoi(?:\s+(?P<o>{'|'.join(_TENS_ONES)}))?\s*(?P<u>{_UNIT_ALTERNATION})(?![a-z])")
_FRAC = re.compile(r"\s*(?P<f>\d{1,2}(?![\d.,])|ruoi(?![a-z]))")
_WEAK_UNIT = re.compile(r"\bchai\b")  # "2 chai" thường là số chai: chỉ tính là tiền khi câu không có số tiền nào khác
_SPOKEN_MAGNITUDE = {"trăm", "mươi", "mười", "chục"}
_PLAIN = re.compile(r"(?<![\w.,])(?P<n>\d{1,3}(?:[.,]\d{3})+|\d{1,3}(?:\s\d{3})+(?![\d.,])|\d{4,})(?P<suffix>\s*(?:vnd|dong|d)(?![a-z]))?")
# từ chỉ số lượng đứng ngay sau chữ số: "1 triệu 2 ly cafe" thì 2 là số ly. KHÔNG gồm từ chỉ thời gian ("3 củ 8 tháng này": 8 là phần của số tiền).
_COUNT_WORDS = {"ly", "cai", "nguoi", "lan", "chai", "phan", "to", "do", "bo", "chiec", "hop", "kg", "gam", "tieng", "phut", "qua", "goi"}
# Bỏ dấu thì "từ"="tu", "ngân"="ngan", "sau" (sáu/sau): "rút tiền từ ngân hàng" không phải số tiền. Nên chữ "ngan" không dùng ở đường đọc bằng chữ,
# và đoạn chữ không được bắt đầu bằng "tu"/"lam"; chỉ coi là số tiền khi có từ chỉ bậc (trăm, mươi, nghìn, triệu).
_WORD_VOCAB = (_NUMBER_WORDS - {"ngan"}) | set(_WORD_ALIAS)
_WORDS_RUN = re.compile(rf"(?<![a-z])(?:(?:{'|'.join(sorted(_WORD_VOCAB, key=len, reverse=True))})(?![a-z])\s*)+")
_MAGNITUDE = {"tram", "muoi", "moi", "nghin", "nghn", "trieu"}


class AmountMatch(NamedTuple):
    value: int
    start: int  # vị trí trong câu đã chuẩn hóa (NFC, bỏ dấu): cùng độ dài với câu gốc
    end: int
    assumed: bool = False  # số nhỏ viết bằng chữ không đơn vị: đơn vị là nghìn do ĐOÁN, chưa được người dùng xác nhận


def _slang_is_money(unit: str, nfc: str, start: int, end: int, number_start: int | None = None) -> bool:
    """Từ lóng có dấu (tỏi, xị, lốp...) chỉ nhận khi viết đúng dấu, rồi qua cùng bộ lọc "2 củ cải"/"2 lít xăng" với voice."""
    word = _SLANG_WORD.get(unit)
    if word is None:
        return True  # k, nghìn, triệu... không phải từ lóng
    return (nfc[start:end] == word or word in _UNACCENTED_OK) and is_money(word, nfc, end, number_start)


def _slang(norm: str, nfc: str) -> list[AmountMatch]:
    found = []
    for m in _SLANG.finditer(norm):
        if not _slang_is_money(m.group("u"), nfc, m.start("u"), m.end("u"), m.start("n")):
            continue
        unit = UNITS[m.group("u")]
        value, end = float(m.group("n").replace(",", ".")) * _MULTIPLIER.get(m.group("m"), 1) * unit, m.end()
        thousands = re.match(r"\d{3}(?![\d.,])", norm[end:]) if m.group("u") in ("tr", "trieu") else None
        if thousands:  # "1tr350" = 1 triệu 350 nghìn; hai chữ số ("1tr35") là phần thập phân, đi đường _FRAC bên dưới
            value += int(thousands.group()) * 1_000
            end += 3
        frac = _FRAC.match(norm, end) if not (m.group("m") or thousands) and (m.group("u") != "k" or norm[end:end + 1].isdigit()) else None
        if frac:
            nxt = re.match(r"\s*([a-z]+)", norm[frac.end():])
            token = frac.group("f")
            if not (token.isdigit() and nxt and nxt.group(1) in _COUNT_WORDS):  # "1 triệu 2 ly cafe": 2 là số ly, không phải 0,2 triệu
                value += (0.5 if token == "ruoi" else int(token) / 10 ** len(token)) * unit
                end = frac.end()
        found.append(AmountMatch(round(value), m.start(), end))
    for m in _DIGIT_TENS.finditer(norm):
        if _slang_is_money(m.group("u"), nfc, m.start("u"), m.end("u"), m.start("d")):
            tens = int(m.group("d")) * 10 + _TENS_ONES.get(m.group("o"), 0)
            found.append(AmountMatch(tens * UNITS[m.group("u")], m.start(), m.end()))
    for m in re.finditer(r"(?<![a-z])(?P<u>cu|lit|trieu|nghin)\s+ruoi(?![a-z])", norm):  # "củ rưỡi", "lít rưỡi" không có số đứng trước
        before = norm[:m.start()].split()
        if before and (before[-1] in _NUMBER_WORDS or before[-1].isdigit()):
            continue  # "ba triệu rưỡi": số đứng trước, để đường đọc bằng chữ tính (3.500.000), không phải 1,5 triệu
        if not any(f.start <= m.start() < f.end for f in found):
            found.append(AmountMatch(round(1.5 * UNITS[m.group("u")]), m.start(), m.end()))
    return found


def _plain(norm: str) -> list[AmountMatch]:
    found = []
    for m in _PLAIN.finditer(norm):
        digits = re.sub(r"\D", "", m.group("n"))
        if re.search(r"\s", m.group("n")) and not (m.group("suffix") or digits.endswith("000")):
            continue  # "45 000 đồng", "120 000đ", "45 000": dấu cách ngăn nghìn chỉ tin khi có đơn vị tiền hoặc tròn nghìn; "3 120" có thể là hai số khác nhau
        if not m.group("suffix") and len(digits) == 4 and 1900 <= int(digits) <= 2100 and not re.search(r"[.,]", m.group("n")):
            continue  # năm, không phải tiền
        if 1_000 <= int(digits) <= 999_999_999:
            found.append(AmountMatch(int(digits), m.start(), m.end()))
    return found


def _thousand_marked(norm: str, original: str) -> str:
    """"ngàn" (có dấu) là đơn vị tiền; "ngan" không dấu lẫn với "ngân hàng". Đổi riêng "ngàn" thành "nghn" (cùng độ dài, vị trí không lệch)."""
    return re.sub(r"ngan", lambda m: "nghn" if original[m.start():m.end()] == "ngàn" else m.group(0), norm)


def _words(norm: str) -> list[AmountMatch]:
    found = []
    for m in _WORDS_RUN.finditer(norm):
        run = m.group(0).strip()
        words = run.split()
        if words[0] in ("tu", "lam") or not _MAGNITUDE.intersection(words):
            continue
        value = words_to_amount(run)
        if value is None:
            continue
        end = m.end()
        half = re.match(r"\s*ruoi(?![a-z])", norm[end:])  # "một triệu rưỡi", "hai trăm nghìn rưỡi" hiếm nhưng "triệu rưỡi" thường gặp
        bonus = {"trieu": 500_000, "nghin": 500, "nghn": 500, "ngan": 500, "tram": 50}.get(words[-1])  # "hai trăm rưỡi" = 250 (không tự nhân 1000, xem number_words)
        if half and bonus:
            value += bonus
            end += half.end()
        found.append(AmountMatch(value, m.start(), end))
    return found


def _spoken(norm: str, nfc: str) -> list[AmountMatch]:
    """Số đọc bằng chữ. Câu có dấu đi qua bộ đọc dùng chung với voice (từ lóng, tỷ, chục, rưỡi...); không đọc được thì thử đường cũ."""
    if nfc != norm:
        found = [
            AmountMatch(m.value, m.start, m.end)
            for m in find_number_words(nfc)
            if m.has_scale or _SPOKEN_MAGNITUDE.intersection(re.findall(r"[^\W\d_]+", nfc[m.start:m.end]))  # "ba ly cà phê": ba là số lượng
        ]
        if found:
            return found
    return _words(_thousand_marked(norm, nfc))


_K_UNIT = re.compile(r"\s*(?P<u>ka|k|ca)(?![a-z])")
# "ca" đứng một mình sau số là chữ "k" đọc theo tiếng Việt ("năm mươi ca" = 50k), nhưng cũng là danh từ ("hai ca trực", "ca cao"): chỉ nhận khi hết câu,
# sau dấu câu, hoặc ngay trước một liên từ/trạng từ thường gặp, và chỉ với "ca" không dấu ("cà phê", "cá" không phải đơn vị).
_CA_FOLLOWERS = {"va", "roi", "nhe", "nha", "thoi", "xong", "sau", "truoc", "voi", "luc", "hom"}


_PERCENT_AFTER = re.compile(r"\s*(?:phan\s+tram|%)")
_CURRENCY_AFTER = re.compile(r"\s*(?:dong|vnd|d)(?![a-z])")  # "bốn mươi lăm đồng": đơn vị rõ ràng, hiểu đúng 45 đồng
# danh từ đếm/thời gian đứng ngay sau số: "hai mươi người", "ba mươi ngày" là số lượng, không phải tiền
_COUNT_AFTER = re.compile(r"\s*(?:nguoi|cai|ly|to|chai|lan|chuyen|ngay|thang|nam|tuoi|gio|phut|giay|kg|ky|lang|lit|con|chiec|bo|cuon|qua|trai|hop|goi|vien|cay|ban|phong|tang|bua|dem|tuan)(?![a-z])")


def _extend_with_k_unit(find):
    """Số đọc bằng chữ nhỏ hơn 1.000 mà liền sau là "k"/"ka"/"ca" thì là nghìn và nuốt luôn chữ đơn vị (50k = "năm mươi k"/"năm mươi ca")."""

    @functools.wraps(find)
    def wrapper(text: str) -> list[AmountMatch]:
        norm = normalize(text)
        nfc = unicodedata.normalize("NFC", text).lower()
        out = []
        for m in find(text):
            spoken = m.value < 1000 and not any(c.isdigit() for c in nfc[m.start:m.end])
            unit = _K_UNIT.match(norm, m.end) if spoken else None
            if unit and nfc[unit.start("u"):unit.end("u")] == unit.group("u"):
                rest = norm[unit.end():]
                follower = re.match(r"\s*([a-z]+)", rest)
                if unit.group("u") != "ca" or not rest.strip() or rest.lstrip()[:1] in ",.;!?" or (follower and follower.group(1) in _CA_FOLLOWERS):
                    m = AmountMatch(m.value * 1000, m.start, unit.end())
                    out.append(m)
                    continue
            if spoken:
                tail = norm[m.end:]
                if _PERCENT_AFTER.match(tail):
                    continue  # "giảm năm mươi phần trăm" là tỷ lệ, không phải số tiền
                if m.value > 0 and not (_CURRENCY_AFTER.match(tail) or _COUNT_AFTER.match(tail)):
                    m = m._replace(assumed=True)  # không đơn vị: bên dựng khoản chi hiểu là nghìn và bắt xác nhận
            out.append(m)
        return out

    return wrapper


@_extend_with_k_unit
def find_amounts(text: str) -> list[AmountMatch]:
    """Mọi số tiền trong câu, theo thứ tự xuất hiện (``parse_amount`` chỉ lấy số cuối). start/end tính trên câu đã chuẩn hóa; xem normalize()."""
    norm = normalize(text)
    nfc = unicodedata.normalize("NFC", text).lower()
    candidates = _slang(norm, nfc) + _plain(norm)  # số viết bằng chữ số được ưu tiên; chỉ khi không có mới đọc số viết bằng chữ
    if not candidates:
        candidates = _spoken(norm, nfc)
    # bỏ ứng viên nằm trọn trong ứng viên khác (số "1.5" trong "1.5tr" cũng khớp dạng thường)
    candidates = [c for c in candidates if not any(o is not c and o.start <= c.start and c.end <= o.end and (o.end - o.start) > (c.end - c.start) for o in candidates)]
    firm = [c for c in candidates if not _WEAK_UNIT.search(nfc[c.start:c.end])]
    return sorted(firm or candidates, key=lambda c: c.start)


def parse_amount(text: str) -> AmountMatch | None:
    """Số tiền trong câu (số xuất hiện cuối cùng), hoặc None. start/end tính trên câu đã chuẩn hóa; xem normalize()."""
    found = find_amounts(text)
    return found[-1] if found else None


def normalize(text: str) -> str:
    """NFC rồi bỏ dấu, chữ thường: cùng độ dài với câu gốc (NFC) nên vị trí khớp dùng lại được cho câu gốc."""
    return fold(unicodedata.normalize("NFC", text))


def remove_amount(text: str, match: AmountMatch) -> str:
    """Câu gốc đã bỏ phần số tiền (và khoảng trắng thừa)."""
    nfc = unicodedata.normalize("NFC", text)
    return re.sub(r"\s+", " ", nfc[:match.start] + " " + nfc[match.end:]).strip(" ,.-")
