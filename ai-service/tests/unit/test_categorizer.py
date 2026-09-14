"""Categorizer — nhánh từ điển và ràng buộc taxonomy."""

import pytest

from app.core.enums import CATEGORY_SLUGS, TransactionType
from app.pipeline.stages.categorizer import categorize_by_rules, load_lexicon


@pytest.mark.parametrize(
    ("text", "slug"),
    [
        ("HIGHLANDS COFFEE", "food"),
        ("GRAB", "transport"),
        ("SHOPEE", "shopping"),
        ("EVN HCMC", "bills"),
        ("CGV CINEMAS", "entertainment"),
        ("NHA THUOC LONG CHAU", "health"),
        ("HOC PHI DH BACH KHOA", "education"),
        ("TIEN NHA THANG 9", "housing"),
    ],
)
def test_lexicon_maps_known_merchants(text, slug):
    assert categorize_by_rules(text).category_slug == slug


def test_longer_keyword_wins():
    """ "the coffee house" phải thắng "cafe": khớp từ ngắn trước thì mọi chuỗi chứa nó
    đều rơi vào cùng một danh mục."""
    assert categorize_by_rules("THE COFFEE HOUSE").category_slug == "food"


def test_unknown_merchant_falls_back_with_low_confidence():
    """Trả "other" nhưng confidence thấp, để ngưỡng 85% của Flow 1 đẩy sang hộp thoại
    cho người dùng chọn thay vì xác nhận một chạm vào danh mục sai."""
    outcome = categorize_by_rules("XYZQWE KHONG CO TRONG TU DIEN")
    assert outcome.category_slug == "other"
    assert outcome.confidence < 0.85


def test_lexicon_hit_clears_the_one_tap_threshold():
    """Khớp chính xác tên cửa hàng phải đủ tin để Flow 1 bước 5.2 cho xác nhận một chạm.

    Dưới 0,85 thì MỌI khoản chi đều rớt xuống nhánh hộp thoại — kể cả ca "Highlands Coffee"
    mà chính docx lấy làm ví dụ cho nhánh một chạm, tức là tính năng không bao giờ chạy.
    """
    assert categorize_by_rules("HIGHLANDS COFFEE").confidence >= 0.85


def test_every_lexicon_slug_is_a_system_category():
    """Slug ngoài 11 danh mục hệ thống làm GetSystemBySlugAsync phía backend trả NULL và
    giao dịch mất danh mục mà không báo lỗi ở đâu."""
    assert {slug for _, slug in load_lexicon()} <= set(CATEGORY_SLUGS)


def test_credit_is_always_income():
    from app.pipeline.models.model_registry import ModelRegistry
    from app.pipeline.stages.categorizer import Categorizer

    outcome = Categorizer(ModelRegistry()).categorize(
        "HIGHLANDS COFFEE", "tra luong", TransactionType.CREDIT
    )
    assert outcome.category_slug == "income"


def test_lexicon_on_full_corpus(corpus):
    """Ghi nhận mức hiện tại của nhánh từ điển làm mốc chống hồi quy.

    Ngưỡng đặt ở 95% chứ không phải con số đo được: từ điển được viết ra từ chính danh
    sách merchant của corpus bootstrap nên điểm này lạc quan có hệ thống.
    """
    debits = [
        row
        for row in corpus
        if row["label"]["is_financial"] and row["label"]["transaction_type"] == "debit"
    ]
    correct = sum(
        categorize_by_rules(
            f"{row['label']['merchant_name'] or ''} {row['body']}"
        ).category_slug
        == row["label"]["category_slug"]
        for row in debits
    )
    assert correct / len(debits) >= 0.95
