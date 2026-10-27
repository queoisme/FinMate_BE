"""Danh mục có đúng MỘT nguồn: ocr-service/data/category_groups.json (9 nhóm hóa đơn + quy đổi sang slug backend) và CATEGORY_SLUGS (hợp đồng với backend).
Các nơi khác chỉ đọc lại; test này canh để không ai chép thêm một bản rồi để nó lệch."""
from app.bill import gemini, lexicon
from app.bill.classifier import CATEGORIES
from app.core.category_map import SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.db.models.bill import BILL_CATEGORY_GROUPS

DOCUMENT = lexicon.load("category_groups.json")


def test_every_place_that_lists_the_nine_groups_reads_the_shared_file():
    groups = tuple(DOCUMENT["groups"])
    assert len(groups) == len(set(groups)) and "other" in groups
    assert CATEGORIES == groups and BILL_CATEGORY_GROUPS == groups


def test_the_backend_slug_map_covers_exactly_the_groups_and_only_valid_slugs():
    assert set(SLUG_BY_LOCAL_CATEGORY) == set(DOCUMENT["groups"])  # thiếu nhóm thì khoản đó mất danh mục một cách im lặng
    assert set(SLUG_BY_LOCAL_CATEGORY.values()) <= set(CATEGORY_SLUGS) - {"income"}


def test_the_gemini_schema_follows_the_backend_slugs():
    assert gemini.CATEGORIES == [s for s in CATEGORY_SLUGS if s != "income"] + ["unknown"]
