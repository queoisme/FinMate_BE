import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

from category_model import CategoryModel, record_text, train
from extract import classify_category_detailed

# Dữ liệu tổng hợp: mỗi danh mục có từ vựng đặc trưng; mỗi cửa hàng xuất hiện 2 lần.
_VOCAB = {
    "groceries": ["gao thom", "nuoc mam", "rau muong", "thit heo", "trung ga", "sua tuoi", "dau an", "duong cat"],
    "dining": ["ca phe sua da", "tra dao cam sa", "banh mi thit", "bun bo hue", "com tam suon", "tra sua tran chau", "nuoc cam ep", "sinh to bo"],
    "healthcare": ["paracetamol 500mg", "vitamin c", "khau trang y te", "thuoc ho", "bong gac", "nuoc muoi sinh ly", "amoxicillin", "oresol"],
}


def _neutral_name(i: int) -> str:
    """Tên cửa hàng vô nghĩa, khác nhau cho từng cửa hàng và không gợi ý danh mục (tránh rò nhãn)."""
    letters = "kvmrtzbnlp"
    return "tiem " + "".join(letters[(i // 10 ** d) % 10] for d in range(3)) + "ae"


def _rows(per_class_merchants=6, repeats=2):
    rows, shop = [], 0
    for cat, words in _VOCAB.items():
        for m in range(per_class_merchants):
            shop += 1
            for r in range(repeats):
                rows.append({
                    "merchant": _neutral_name(shop),
                    "category": cat,
                    "lines": [f"{words[(m + r) % len(words)]} x{r + 1}", words[(m + r + 3) % len(words)], "TONG 100.000"],
                })
    return rows


def test_record_text_drops_numbers_and_amounts():
    text = record_text({"merchant": "Kisnac", "lines": ["TONG 570,500", "Ma HD 123", "Nuoc mam"]})
    assert not any(ch.isdigit() for ch in text)
    assert "kisnac" in text and "nuoc mam" in text


def test_trains_and_reports_grouped_accuracy_on_enough_data():
    report = train(_rows())
    assert report.trained is True
    assert report.n_records == 36 and report.classes == {"groceries": 12, "dining": 12, "healthcare": 12}
    assert report.cv_accuracy >= 0.9
    assert report.model is not None


def test_refuses_to_train_with_too_little_data():
    report = train(_rows(per_class_merchants=1, repeats=2))   # 6 hóa đơn
    assert report.trained is False and report.model is None
    assert "30" in report.reason


def test_refuses_when_accuracy_is_below_threshold():
    rows = _rows()
    for r in rows:                      # chữ giống hệt nhau giữa các danh mục: không thể học được
        r["lines"] = ["giong nhau"]
    report = train(rows)
    assert report.trained is False and "chính xác" in report.reason


def test_cross_validation_never_puts_one_merchant_in_both_train_and_test():
    # Mỗi cửa hàng có 2 hóa đơn giống nhau; nếu CV không nhóm theo cửa hàng, độ chính xác sẽ ảo (~100%)
    rows = _rows()
    for r in rows:
        r["lines"] = [r["merchant"]]     # từ vựng chỉ là tên cửa hàng, không suy ra được cho cửa hàng mới
    report = train(rows)
    assert report.cv_accuracy < 0.8


def test_save_load_predict_roundtrip(tmp_path):
    model = train(_rows()).model
    path = tmp_path / "m.joblib"
    CategoryModel(model).save(path)
    loaded = CategoryModel.load(path)
    category, prob = loaded.predict("Tiem la", ["paracetamol 500mg", "vitamin c", "TONG 50.000"])
    assert category == "healthcare" and prob > 0.5


def test_load_returns_none_when_no_file(tmp_path):
    assert CategoryModel.load(tmp_path / "missing.joblib") is None


def test_classify_uses_model_last_and_never_above_review_threshold():
    guess = lambda m, l: ("healthcare", 0.9)
    assert classify_category_detailed("Tiem la", ["abc"], predict=guess) == ("healthcare", "model", 0.69)
    assert classify_category_detailed("KISNAC", [], predict=lambda m, l: ("dining", 0.99))[1] == "brand"
    assert classify_category_detailed("ABC", [], remembered="shopping", predict=guess)[1] == "memory"
    assert classify_category_detailed("ABC", [], predict=lambda m, l: ("other", 0.9)) == ("other", "none", 0.0)
    assert classify_category_detailed("ABC", [], predict=lambda m, l: ("dining", 0.4)) == ("other", "none", 0.0)
