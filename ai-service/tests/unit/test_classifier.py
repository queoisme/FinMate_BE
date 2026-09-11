"""Nhánh luật của Classifier — nhánh chạy ở mọi lần triển khai chưa có model."""

import pytest

from app.core.enums import ClassifierLabel
from app.pipeline.stages.classifier import MANUAL_ENTRY_PACKAGE, classify_by_rules


@pytest.mark.parametrize(
    "body",
    [
        "TK 0123456789|GD: -75,000VND 09/09/2026 10:28|SD: 2,500,000VND|ND: TT QR HIGHLANDS",
        "So du TK VCB 0011001234567 -250,000 VND luc 09-09-2026 19:05:00. So du 1,750,000 VND",
        "Bạn đã thanh toán 75.000đ cho Highlands Coffee. Số dư: 2.500.000đ",
    ],
)
def test_balance_notifications_are_financial(body):
    assert classify_by_rules(body, "com.mbmobile").label == ClassifierLabel.FINANCIAL


@pytest.mark.parametrize(
    "body",
    [
        # OTP có 6 chữ số — dễ bị nhầm là số tiền nếu chỉ đếm chữ số.
        "Ma OTP cua quy khach la 483920. Ma co hieu luc trong 3 phut, tuyet doi khong chia se.",
        "Uu dai thang nay: giam 50% phi chuyen tien quoc te khi giao dich tren app MB Bank.",
        "Chuc mung nam moi! MB Bank kinh chuc quy khach mot nam an khang thinh vuong.",
        "Nhac no the tin dung: quy khach vui long thanh toan truoc ngay 15 de tranh phi tre han.",
    ],
)
def test_promotional_and_otp_notifications_are_not_financial(body):
    assert (
        classify_by_rules(body, "com.mbmobile").label == ClassifierLabel.NON_FINANCIAL
    )


def test_number_without_currency_unit_is_not_money():
    """Dãy số trần thường là mã OTP hay số tài khoản, không phải tiền."""
    outcome = classify_by_rules("Ma xac thuc: 998877", "com.mbmobile")
    assert outcome.label == ClassifierLabel.NON_FINANCIAL


def test_amount_without_transaction_context_is_uncertain():
    """Có tiền nhưng không có bối cảnh giao dịch: đẩy sang uncertain để backend còn hỏi
    người dùng, thay vì im lặng bỏ qua một giao dịch thật."""
    outcome = classify_by_rules("Han muc con lai cua ban: 5.000.000d", "com.unknown")
    assert outcome.label == ClassifierLabel.UNCERTAIN


def test_manual_entry_skips_the_financial_gate():
    """Người dùng chủ động gõ câu chi tiêu — ý định đã rõ trước khi gửi lên, áp cổng phân
    loại vào đây chỉ chặn nhầm chính họ."""
    outcome = classify_by_rules("trưa nay ăn phở 45k ở Phở Thìn", MANUAL_ENTRY_PACKAGE)
    assert outcome.label == ClassifierLabel.FINANCIAL


def test_manual_entry_without_amount_is_uncertain():
    outcome = classify_by_rules("hôm nay đi chơi với bạn", MANUAL_ENTRY_PACKAGE)
    assert outcome.label == ClassifierLabel.UNCERTAIN


def test_rule_branch_on_full_corpus(corpus):
    """Toàn corpus phải đúng — đây là lưới chặn hồi quy khi sửa danh sách từ khoá.

    Lưu ý: corpus bootstrap là dữ liệu TỔNG HỢP sinh từ chính các template mà luật đã
    biết, nên 100% ở đây KHÔNG phải là độ chính xác ngoài đời.
    """
    wrong = [
        row["body"]
        for row in corpus
        if (
            classify_by_rules(
                f"{row['title'] or ''} {row['body']}", row["package_name"]
            ).label
            == ClassifierLabel.FINANCIAL
        )
        != row["label"]["is_financial"]
    ]
    assert wrong == []
