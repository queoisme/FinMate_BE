"""Che dữ liệu nhạy cảm trước khi vào AI DB (AGENTS.md §3.1, §3.2)."""

from app.core.anonymizer import anonymize_body, content_hash, hash_user_id, mask_digits


def test_account_number_is_masked_but_amount_survives():
    """Số tiền PHẢI sống sót: nó là nhãn của chính bài toán, che nhầm là xoá mất dữ liệu
    huấn luyện."""
    body = "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS"
    masked = anonymize_body(body)
    assert "0123456789" not in masked
    assert "xxxxxx6789" in masked
    assert "75,000VND" in masked
    assert "2,500,000VND" in masked
    assert "HIGHLANDS" in masked


def test_amount_written_without_separator_is_not_mistaken_for_an_account():
    assert anonymize_body("So du 2500000VND") == "So du 2500000VND"


def test_card_number_phone_and_email_are_masked():
    masked = anonymize_body(
        "The 9704 1234 5678 9012 lien he abc.def@bank.vn hoac 0912345678"
    )
    assert "9704 1234 5678" not in masked
    assert "xxxx xxxx xxxx 9012" in masked
    assert "abc.def@bank.vn" not in masked
    assert "<email>" in masked
    assert "0912345678" not in masked


def test_mask_digits_keeps_last_four():
    assert mask_digits("0123456789") == "xxxxxx6789"
    assert mask_digits("123") == "xxx"


def test_hash_user_id_is_sha256_hex():
    digest = hash_user_id("11111111-1111-1111-1111-111111111111")
    assert len(digest) == 64
    assert int(digest, 16) >= 0


def test_content_hash_ignores_time_but_not_content():
    """Cùng nội dung nhận ở hai thời điểm vẫn là MỘT mẫu huấn luyện."""
    first = content_hash("com.mbmobile", "MB", "GD: -75,000VND")
    second = content_hash("com.mbmobile", "MB", "GD: -75,000VND")
    third = content_hash("com.mbmobile", "MB", "GD: -76,000VND")
    assert first == second
    assert first != third
