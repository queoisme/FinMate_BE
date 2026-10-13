"""Tách một câu chat thành nhiều khoản: "ăn cơm 20k, uống nước 10k và đổ xăng 50k".

Chỉ tách khi CẢ HAI phía của dấu phân cách đều có số tiền và vế sau có chữ mô tả: nhờ vậy dấu phẩy thập phân ("1,5tr"), dấu chấm hàng nghìn,
số lượng ("mua 2 lít sữa 50k"), vế phụ ("ăn cơm 20k, 2 người") và phép cộng ("ăn 20k + 30k") vẫn là MỘT khoản.
"""
from __future__ import annotations

import re
import unicodedata

from app.services.vi_text import fold
from app.services.amount_normalizer import find_amounts, parse_amount, remove_amount

# Dấu phẩy nằm giữa hai chữ số ("1,5tr") không phải dấu phân cách.
_SEPARATOR = re.compile(
    r"(\s*(?<!\d),\s*|\s*,(?!\d)\s*|\s*;\s*|\s+(?:và|va|rồi|roi|sau đó|sau do)\s+|\s*\+\s*)", re.IGNORECASE
)
_WORD = re.compile(r"[^\W\d_]{2,}")


def _is_clause(part: str) -> bool:
    match = parse_amount(part)
    return bool(match) and bool(_WORD.search(remove_amount(part, match)))


def split_clauses(text: str) -> list[str]:
    pieces = _SEPARATOR.split(text)
    parts, separators = pieces[0::2], pieces[1::2]
    clauses = [parts[0]]
    for separator, part in zip(separators, parts[1:]):
        if parse_amount(clauses[-1]) and _is_clause(part):
            clauses.append(part)
        else:
            clauses[-1] += separator + part
    return [clause.strip() for clause in clauses] if len(clauses) > 1 else [text]


# Một vế chỉ gồm lời hủy ("hủy", "thôi", "không mua nữa"): bỏ khoản đứng ngay trước nó. "không cần hủy" hay "hủ tiếu" không khớp vì
# phải là cả vế.
_CANCEL = re.compile(
    r"^\s*(?:(?:rồi|và|à|ờ|ơ)\s+)*(?:thôi\s+)?(?:hủy|huỷ|bỏ|khỏi|không\s+mua\s+nữa)(?:\s+(?:đi|nhé|nha|luôn|rồi|khoản\s+đó|cái\s+đó))?\s*$",
    re.IGNORECASE,
)
# Còn sót chữ hủy/nhầm ở đâu đó trong câu (không vế nào gồm riêng lời hủy): không được tự ghi, để người dùng xem lại
_CANCEL_WORD = re.compile(r"(?<!\w)(?:hủy|huỷ|nhầm)(?!\w)", re.IGNORECASE)


def has_unresolved_cancel(text: str) -> bool:
    return bool(_CANCEL_WORD.search(text))
_CORRECTION = re.compile(r"^\s*(?:à\s+|ờ\s+)?(?:nhầm|sai\s+rồi|nhầm\s+rồi)\s*$", re.IGNORECASE)


def apply_cancellations(text: str) -> tuple[str, bool]:
    """Áp lời hủy và lời sửa trong câu: trả (câu đã bỏ các khoản bị hủy / đã đổi tiền, có áp dụng gì không)."""
    if _CANCEL_WORD.search(text):
        text = re.sub(r"(?<!\d)\.\s+", ", ", text)  # "30k. Hủy": dấu chấm cũng ngăn các vế
    pieces = _SEPARATOR.split(text)
    parts, separators = pieces[0::2], [""] + pieces[1::2]
    kept: list[tuple[str, str]] = []
    changed = correcting = False
    for separator, part in zip(separators, parts):
        if _CANCEL.match(part):
            while kept and not parse_amount(kept[-1][1]):  # vế phụ ("2 người") đứng giữa không phải khoản bị hủy
                kept.pop()
            if kept:
                kept.pop()
            changed = True
        elif _CORRECTION.match(part):
            correcting = True
            changed = True
        elif correcting and (new := parse_amount(part)) and kept:
            earlier_sep, earlier = kept[-1]
            old = parse_amount(earlier)
            if old:
                kept[-1] = (earlier_sep, remove_amount(earlier, old).strip() + " " + part[new.start:new.end])
            correcting = False
        else:
            kept.append((separator, part))
    if not changed:
        return text, False
    return "".join((sep if i else "") + part for i, (sep, part) in enumerate(kept)).strip(), True


# Nhãn tổng đứng riêng như một từ và liền ngay sau là số tiền ("tổng cộng 60k", "tổng là 60k"); "bộ tổng hợp đề 150k" không khớp.
_TOTAL_LABEL = re.compile(r"(?<!sếp\s)(?<!\w)(?:tổng\s+cộng|tổng\s+số|tổng|tất\s+cả)(?:\s*[:：]|\s+là|\s+hết)?\s*", re.IGNORECASE)


def extract_total(text: str) -> tuple[str, int | None]:
    """Tách số tiền tổng người dùng nói ra khỏi câu: trả (câu không còn phần tổng, số tiền tổng).

    Chỉ tách khi phần còn lại vẫn có số tiền, để câu chỉ có "tổng 60k" vẫn được coi là một khoản như trước.
    """
    # "trong 100k có 20k ăn cơm và 80k mua sữa" / "100k gồm ăn cơm 20k và ...": số đầu câu là tổng, phần sau là các khoản
    lead = re.match(r"\s*(?:trong\s+)?", text)
    first = find_amounts(text[lead.end():])[:1]
    if first and first[0].start == 0:
        after = text[lead.end():][first[0].end:]
        keyword = _BREAKDOWN_KEYWORD.match(after)
        if keyword and parse_amount(after[keyword.end():]):
            return after[keyword.end():], first[0].value
    for label in _TOTAL_LABEL.finditer(text):
        amount = parse_amount(text[label.end():])
        if not amount or amount.start > 0:  # số tiền phải liền ngay sau nhãn
            continue
        rest = (text[:label.start()] + text[label.end() + amount.end:]).strip(" ,;.")
        if parse_amount(rest):
            return rest, amount.value
    return text, None


_FILLER = {"nhé", "nha", "nhá", "luôn", "nữa", "giúp", "với", "đấy", "ạ", "nhỉ"}
# Vế mở đầu bằng những từ này là người/nơi/lời đệm đi kèm khoản trước ("... cho mẹ và em", "... rồi về nhà"), không phải một món thiếu tiền
_NOT_AN_ITEM = {"về", "với", "cho", "em", "anh", "chị", "mẹ", "bố", "ba", "má", "con", "bạn", "mình", "nhà", "ở", "tại", "hết", "xong", "luôn", "nữa"}
_WITH_PEOPLE = re.compile(r"(?<!\w)(?:với|cho)(?!\w)", re.IGNORECASE)


def split_amountless(text: str) -> tuple[str, tuple[str, ...]]:
    """Vế cuối nối bằng "và/rồi" hoặc dấu phẩy mà không có số tiền ("ăn phở 40k và cà phê") không bị gộp vào mô tả: tách ra để báo.

    Chỉ áp khi phần đầu có số tiền. Vế phụ ("2 người", bắt đầu bằng chữ số), lời đệm ("nhé"), người/nơi đi kèm ("cho mẹ và em",
    "rồi về nhà") vẫn nằm trong khoản.
    """
    pieces = _SEPARATOR.split(text)
    parts, separators = pieces[0::2], pieces[1::2]
    if len(parts) < 2 or not parse_amount(parts[0]):
        return text, ()
    kept, skipped = [(None, parts[0])], []
    for separator, piece in zip(separators, parts[1:]):
        words = _WORD.findall(piece)
        first = (words[0] if words else "").lower()
        with_people = bool(_WITH_PEOPLE.search(kept[-1][1])) and len(words) <= 2
        if (parse_amount(piece) or not words or piece.strip()[0].isdigit() or all(w.lower() in _FILLER for w in words)
                or first in _NOT_AN_ITEM or with_people):
            kept.append((separator, piece))
        else:
            skipped.append(piece.strip(" ,;."))
    if not skipped:
        return text, ()
    return "".join((sep if sep is not None else "") + part for sep, part in kept), tuple(skipped)


_VOLUME = re.compile(r"(?<![a-z])lit(?![a-z])")


def split_on_amounts(text: str) -> list[str]:
    """Tách các khoản không có dấu phẩy/"và" ngăn cách (giọng nói không có dấu câu): "ăn cơm 25k uống nước 10k" -> hai khoản.

    Câu viết kiểu "mô tả rồi tiền" cắt ngay SAU mỗi số tiền; câu viết kiểu "tiền rồi mô tả" ("25k ăn cơm 10k uống nước") cắt ngay TRƯỚC số
    tiền kế. Chỉ cắt khi giữa hai số tiền có chữ mô tả, và "2 lít" (thể tích, không phải 200.000) không được coi là số tiền, nên
    "mua 2 lít sữa 50k" và "mua sữa 50k hộp 2 lít" vẫn là một khoản.
    """
    nfc = unicodedata.normalize("NFC", text)
    found = [m for m in find_amounts(text) if not _VOLUME.search(fold(nfc[m.start:m.end]))]
    if len(found) < 2:
        return [text]
    amount_first = not _WORD.search(nfc[: found[0].start])
    cuts = [nxt.start if amount_first else prev.end for prev, nxt in zip(found, found[1:]) if _WORD.search(nfc[prev.end : nxt.start])]
    if not cuts:
        return [text]
    bounds = [0, *cuts, len(nfc)]
    pieces = (re.sub(r"\s+(?:và|va|rồi|roi)$", "", nfc[a:b].strip(" ,;"), flags=re.IGNORECASE) for a, b in zip(bounds, bounds[1:]))  # "... và" cuối vế là từ nối
    return [piece for piece in pieces if piece]


_BREAKDOWN_KEYWORD = re.compile(r"\s*(?:bao gồm|gồm có|trong đó|gồm|có)\s+", re.IGNORECASE)
