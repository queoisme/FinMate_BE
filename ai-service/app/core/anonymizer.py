"""Băm định danh và che dữ liệu nhạy cảm trước khi lưu vào AI DB.

AGENTS.md §3.1: AI DB không bao giờ giữ ``user_id`` thật. §3.2: không log
``notification_body`` gốc. Kho ``raw_samples`` tồn tại để train nên phải giữ được nội
dung, nhưng nội dung đó đã đi qua ``anonymize_body`` — số tài khoản, số thẻ, số điện
thoại và email bị che, còn cấu trúc câu và số tiền giữ nguyên vì đó chính là thứ model
cần học.
"""

from __future__ import annotations

import hashlib
import re

# Dãy số dài không có dấu ngăn nghìn và không đi kèm ký hiệu tiền tệ → số tài khoản/thẻ/
# điện thoại. Điều kiện "không đi kèm ký hiệu tiền tệ" là bắt buộc: vài app viết số tiền
# liền không dấu ("2500000VND"), che nhầm là xoá mất nhãn của chính bài toán.
_LONG_DIGITS = re.compile(
    r"(?<![\d.,])(\d{8,19})(?![\d.,])(?!\s*(?:vnd|vnđ|đ|₫))",
    re.IGNORECASE,
)

# Số thẻ viết theo nhóm 4: 9704 1234 5678 9012 hoặc 9704-1234-5678-9012.
_CARD_GROUPS = re.compile(r"\b(\d{4})[\s-](\d{4})[\s-](\d{4})[\s-](\d{2,4})\b")

_EMAIL = re.compile(r"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")


def sha256_hex(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def hash_user_id(user_id: str) -> str:
    return sha256_hex(user_id)


def content_hash(package_name: str, title: str | None, body: str) -> str:
    """Khoá chống trùng của ``raw_samples``.

    Không đưa thời gian vào: cùng một nội dung nhận ở hai thời điểm vẫn là cùng một mẫu
    huấn luyện. (Khác với dedup của backend — bên đó có tính thời gian vì mục đích là
    chống xử lý lặp một thông báo, không phải chống lặp dữ liệu train.)
    """
    return sha256_hex(f"{package_name}|{title or ''}|{body}")


def mask_digits(value: str) -> str:
    """Giữ lại 4 số cuối để còn đối chiếu được, che phần còn lại."""
    if len(value) <= 4:
        return "x" * len(value)
    return "x" * (len(value) - 4) + value[-4:]


def anonymize_body(body: str) -> str:
    masked = _CARD_GROUPS.sub(lambda m: f"xxxx xxxx xxxx {m.group(4)}", body)
    masked = _LONG_DIGITS.sub(lambda m: mask_digits(m.group(1)), masked)
    return _EMAIL.sub("<email>", masked)
