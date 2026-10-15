"""Phân loại hóa đơn từ chữ đã lọc: logic chép từ ocr-service, kết quả không được đổi; route chỉ trả danh mục."""

import httpx
import pytest

from app.bill.classifier import classify_category_detailed
from app.core.config import settings
from app.core.enums import CATEGORY_SLUGS
from app.api.v1.bill import get_bill_knowledge
from app.bill import brands, classifier
from app.bill.knowledge import Remembered, apply_rows, merchant_key
from app.main import app

HASH = "a" * 64


class FakeKnowledge:
    """Không chạm DB. ``memory``: {(user_hash, khóa cửa hàng): nhóm trong 9 nhóm} (bảng cũ); ``choices``: {(user_hash, khóa): slug backend} (bảng mới)."""

    def __init__(self, memory=None, choices=None):
        self.memory = memory or {}
        self.choices = choices or {}
        self.refreshes = 0

    async def refresh_if_stale(self):
        self.refreshes += 1

    async def remembered(self, user_id_hash, merchant_name):
        key = (user_id_hash, merchant_key(merchant_name or ""))
        if key in self.choices:
            return Remembered(self.choices[key], True)
        return Remembered(self.memory[key], False) if key in self.memory else None

    async def learn(self, user_id_hash, merchant_name, category_slug):
        key = merchant_key(merchant_name)
        if not key:
            return False
        self.choices[(user_id_hash, key)] = category_slug
        return True


@pytest.fixture(autouse=True)
def knowledge():
    fake = FakeKnowledge()
    app.dependency_overrides[get_bill_knowledge] = lambda: fake
    yield fake
    app.dependency_overrides.pop(get_bill_knowledge, None)


@pytest.fixture
def restore_tables():
    """apply_rows sửa các bảng trong classifier và brands tại chỗ: trả lại nguyên trạng sau mỗi test."""
    saved = (dict(brands.BRAND_CATEGORIES), dict(brands.BRAND_DISPLAY), set(brands.WHOLE_WORD_BRANDS),
             dict(classifier._CATEGORY_KEYWORDS), dict(classifier._PRODUCT_KEYWORDS))
    yield
    for target, value in zip((brands.BRAND_CATEGORIES, brands.BRAND_DISPLAY, brands.WHOLE_WORD_BRANDS,
                              classifier._CATEGORY_KEYWORDS, classifier._PRODUCT_KEYWORDS), saved):
        target.clear()
        target.update(value)


@pytest.mark.parametrize(
    ("merchant", "lines", "category", "source"),
    [
        ("Highlands Coffee", [], "dining", "brand"),
        ("Bách Hóa Xanh", ["Gạo ST25", "Nước mắm"], "groceries", "brand"),
        ("BENHVIEN QUANY103", ["Tong chi phi"], "healthcare", "keyword"),
        ("Lb eyewear", [], "healthcare", "keyword"),
        ("FPT Telecom", [], "utilities", "brand"),
        (None, ["Paracetamol 500mg", "Vitamin C", "Khau trang y te"], "healthcare", "product"),
        (None, ["Cafe PHUONG HOANG LUA", "Da xay tra xanh", "Pass Wifi: 12341234"], "dining", "product"),
        (None, ["Kính râm Rayban", "Tổng 1,500,000"], "shopping", "product"),
    ],
)
def test_known_bills_get_their_category(merchant, lines, category, source):
    assert classify_category_detailed(merchant, lines)[:2] == (category, source)


def test_a_bill_with_nothing_recognisable_is_unknown():
    assert classify_category_detailed("Qwerty Zzz", ["Abc 12", "Tong 50.000"]) == ("other", "none", 0.0)


async def learn(body, headers=True):
    transport = httpx.ASGITransport(app=app)
    auth = {"Authorization": f"Bearer {settings.internal_api_key}"} if headers else {}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post("/api/v1/bill/learn", json={"user_id_hash": HASH, **body}, headers=auth)


async def post(body, headers=True):
    transport = httpx.ASGITransport(app=app)  # không chạy lifespan nên không cần Postgres
    auth = {"Authorization": f"Bearer {settings.internal_api_key}"} if headers else {}
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        return await client.post("/api/v1/bill/classify", json={"user_id_hash": HASH, **body}, headers=auth)


async def test_missing_key_is_rejected():
    assert (await post({"merchant_name": "Highlands Coffee"}, headers=False)).status_code == 401


@pytest.mark.parametrize(
    ("merchant", "slug", "local"),
    [("Highlands Coffee", "food", "dining"), ("Bách Hóa Xanh", "food", "groceries"), ("FPT Telecom", "bills", "utilities"),
     ("Nhà thuốc Long Châu", "health", "healthcare")],
)
async def test_route_returns_a_valid_backend_slug_and_keeps_the_local_group(merchant, slug, local):
    body = (await post({"merchant_name": merchant, "lines": []})).json()
    assert (body["category_slug"], body["local_category"]) == (slug, local)
    assert body["category_slug"] in CATEGORY_SLUGS and body["source"] in {"brand", "keyword", "product", "model"}


async def test_route_confidence_is_capped_because_a_receipt_is_always_reviewed():
    assert (await post({"merchant_name": "Highlands Coffee"})).json()["confidence"] <= 0.80


async def test_unknown_bill_has_no_category_instead_of_a_made_up_one():
    body = (await post({"merchant_name": "Qwerty Zzz", "lines": ["Abc 12", "Tong 50.000"]})).json()
    assert (body["category_slug"], body["local_category"], body["confidence"], body["source"]) == (None, None, 0.0, "none")


async def test_blank_merchant_falls_back_to_the_lines():
    body = (await post({"merchant_name": "   ", "lines": ["Paracetamol 500mg", "Vitamin C"]})).json()
    assert body["category_slug"] == "health"


async def test_input_limits_and_required_hash():
    assert (await post({"lines": ["x"] * 201})).status_code == 422
    assert (await post({"merchant_name": "a" * 201})).status_code == 422
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/bill/classify", json={"user_id_hash": "short"},
            headers={"Authorization": f"Bearer {settings.internal_api_key}"})
    assert response.status_code == 422


async def test_the_users_remembered_shop_beats_the_rules(knowledge):
    knowledge.memory[(HASH, merchant_key("Highlands Coffee"))] = "entertainment"
    body = (await post({"merchant_name": "Highlands Coffee"})).json()
    assert (body["category_slug"], body["local_category"], body["source"]) == ("entertainment", "entertainment", "memory")


async def test_memory_is_per_user(knowledge):
    knowledge.memory[("b" * 64, merchant_key("Highlands Coffee"))] = "entertainment"
    body = (await post({"merchant_name": "Highlands Coffee"})).json()
    assert (body["local_category"], body["source"]) == ("dining", "brand")


async def test_an_unknown_category_in_the_database_is_ignored(knowledge):
    knowledge.memory[(HASH, merchant_key("Highlands Coffee"))] = "income"
    body = (await post({"merchant_name": "Highlands Coffee"})).json()
    assert (body["local_category"], body["source"]) == ("dining", "brand")


async def test_the_route_refreshes_knowledge_before_classifying(knowledge):
    await post({"merchant_name": "Highlands Coffee"})
    assert knowledge.refreshes == 1


def test_merchant_key_matches_the_key_ocr_service_stores():
    assert merchant_key("  Highlands   Coffee ") == merchant_key("HIGHLANDS COFFEE") == "highlands coffee"
    assert merchant_key("Bách Hóa Xanh") == "bach hoa xanh"


def test_database_rows_replace_the_tables_in_code(restore_tables):
    apply_rows(
        [("quan ca phe zzz", "dining", "Quán Cà Phê Zzz", False), ("zzz mart", "groceries", "Zzz Mart", True)],
        [("merchant", "healthcare", "phong kham zzz"), ("product", "entertainment", "ve xem zzz")],
    )
    assert classify_category_detailed("Quán Cà Phê Zzz", [])[:2] == ("dining", "brand")
    assert classify_category_detailed("Phong Kham Zzz", [])[:2] == ("healthcare", "keyword")
    assert classify_category_detailed(None, ["Ve xem Zzz 90.000"])[:2] == ("entertainment", "product")
    assert "zzz mart" in brands.WHOLE_WORD_BRANDS
    assert classify_category_detailed("Highlands Coffee", [])[1] == "none"  # bảng thương hiệu bị thay hẳn, không trộn với bản trong code


def test_empty_database_keeps_the_tables_in_code(restore_tables):
    before = dict(brands.BRAND_CATEGORIES)
    apply_rows([], [])
    assert dict(brands.BRAND_CATEGORIES) == before
    assert classify_category_detailed("Highlands Coffee", [])[:2] == ("dining", "brand")


async def test_a_category_the_user_chose_when_saving_comes_back_as_the_same_backend_slug(knowledge):
    knowledge.choices[(HASH, merchant_key("Highlands Coffee"))] = "entertainment"
    body = (await post({"merchant_name": "Highlands Coffee"})).json()
    assert (body["category_slug"], body["local_category"], body["source"]) == ("entertainment", None, "memory")


async def test_a_choice_beats_the_old_memory_and_the_rules(knowledge):
    key = (HASH, merchant_key("Highlands Coffee"))
    knowledge.memory[key] = "transport"
    knowledge.choices[key] = "health"
    assert (await post({"merchant_name": "Highlands Coffee"})).json()["category_slug"] == "health"


async def test_food_stays_food_because_nothing_is_converted_back_to_nine_groups(knowledge):
    knowledge.choices[(HASH, merchant_key("Tiệm Lạ"))] = "food"
    body = (await post({"merchant_name": "Tiệm Lạ"})).json()
    assert (body["category_slug"], body["local_category"]) == ("food", None)


async def test_learn_remembers_the_choice_for_this_user_only(knowledge):
    response = await learn({"merchant_name": "Highlands Coffee", "category_slug": "entertainment"})
    assert response.status_code == 200 and response.json() == {"learned": True}
    assert (await post({"merchant_name": "Highlands Coffee"})).json()["category_slug"] == "entertainment"
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        other = await client.post("/api/v1/bill/classify", json={"user_id_hash": "b" * 64, "merchant_name": "Highlands Coffee"},
                                  headers={"Authorization": f"Bearer {settings.internal_api_key}"})
    assert other.json()["category_slug"] == "food"  # người khác vẫn theo luật


async def test_learn_rejects_slugs_a_receipt_cannot_have(knowledge):
    for slug in ("income", "dining", "nope", ""):
        assert (await learn({"merchant_name": "Highlands Coffee", "category_slug": slug})).status_code == 422, slug
    assert knowledge.choices == {}


async def test_learn_needs_a_name_that_makes_a_key(knowledge):
    assert (await learn({"merchant_name": "", "category_slug": "food"})).status_code == 422
    assert (await learn({"merchant_name": "a" * 201, "category_slug": "food"})).status_code == 422
    body = (await learn({"merchant_name": "   ", "category_slug": "food"})).json()
    assert body == {"learned": False} and knowledge.choices == {}


async def test_learn_requires_the_internal_key():
    assert (await learn({"merchant_name": "Highlands Coffee", "category_slug": "food"}, headers=False)).status_code == 401


def test_household_consumables_are_shopping():
    # đồ dùng tiêu hao là shopping (quy tắc cũ xếp vào groceries), đi qua đường từ khóa sản phẩm
    for item in ("Bot giat Omo 3kg", "Nuoc rua chen Sunlight", "Giay ve sinh Pulppy", "Nuoc xa vai Comfort"):
        assert classify_category_detailed("Cong ty ABC", [item, "50.000"])[:2] == ("shopping", "product"), item


def test_food_staples_stay_groceries():
    for item in ("Gao ST25 5kg", "Sua tuoi Vinamilk 1L"):
        assert classify_category_detailed("Cong ty ABC", [item, "50.000"])[:2] == ("groceries", "product"), item


def test_a_bach_hoa_store_is_not_a_pharmacy_just_because_its_name_matches_a_pharmacy_chain():
    category, source, _ = classify_category_detailed("BÁCH HÓA MINI AN KHANG", ["Sua tuoi Vinamilk 1L", "Rau muong 500g", "Tong cong 543.000"])
    assert (category, source) == ("groceries", "keyword")


def test_the_pharmacy_chain_alone_is_still_a_pharmacy():
    assert classify_category_detailed("NHÀ THUỐC AN KHANG", ["Paracetamol", "Tong cong 24.000"])[:2] == ("healthcare", "brand")
    assert classify_category_detailed("AN KHANG", ["Tong cong 24.000"])[:2] == ("healthcare", "brand")


# Con Cưng in tên "CON CƯNG" (không kèm "siêu thị/mart/shop"): cửa hàng mẹ và bé, nhóm Gia đình. Bộ luật bill chỉ có 9 nhóm không có "family",
# nên thương hiệu này khai báo thẳng slug backend.
async def test_a_baby_store_is_classified_as_family_straight_to_the_backend_slug(knowledge):
    body = (await post({"merchant_name": "CON CƯNG Mẹ và Bé", "lines": ["Ta dan Bobby", "Sua bot cong thuc", "Tong cong 792.000"]})).json()
    assert (body["category_slug"], body["source"]) == ("family", "brand")


def test_brand_backend_slug_matches_whole_words_only():
    from app.bill.classifier import brand_backend_slug

    assert brand_backend_slug("CON CƯNG Mẹ và Bé") == "family"
    assert brand_backend_slug("Siêu thị Con Cưng Quận 3") == "family"
    assert brand_backend_slug("CON CƯNG") is None  # tên trần có thể là quán cơm
    assert brand_backend_slug("Quán cơm Con Cưng") is None
    assert brand_backend_slug("Highlands Coffee") is None
    assert brand_backend_slug(None) is None


async def test_a_bookstore_bill_whose_items_are_all_one_group_is_education_not_shopping(knowledge):
    # nhà sách Fahasa: cả bill chỉ còn một nhóm nên không tách, và phải theo nhóm của cửa hàng (Giáo dục) thay vì "Mua sắm" của bảng thương hiệu chung
    body = (await post({"merchant_name": "FAHASA", "lines": ["Sach Giai tich", "Vo 200 trang", "Tong cong 508.000"]})).json()
    assert (body["category_slug"], body["source"]) == ("education", "brand")
    from app.bill.classifier import brand_backend_slug

    assert brand_backend_slug("Nhà sách FAHASA") == "education"
