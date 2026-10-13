"""Chia tập dữ liệu phân loại: cùng một cửa hàng không bao giờ nằm ở hai tập (nếu không, kiểm tra trên cửa hàng đã học thuộc)."""
import importlib.util
import pathlib

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "export_category_dataset.py"
spec = importlib.util.spec_from_file_location("export_category_dataset", SCRIPT)
export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(export)


def rows(n_merchants=40, per_merchant=5):
    return [{"text": f"cua hang {m}\nmon {i}", "label": "food", "merchant_key": f"cua hang {m}", "labeled_by": "user"}
            for m in range(n_merchants) for i in range(per_merchant)]


def test_a_merchant_never_lands_in_two_splits():
    train, validation, test = export.split_by_merchant(rows())

    sets = [{r["merchant_key"] for r in part} for part in (train, validation, test)]
    assert not (sets[0] & sets[1]) and not (sets[0] & sets[2]) and not (sets[1] & sets[2])
    assert len(train) + len(validation) + len(test) == 200


def test_the_split_is_deterministic_and_roughly_proportional():
    first = export.split_by_merchant(rows())
    again = export.split_by_merchant(rows())

    assert [len(p) for p in first] == [len(p) for p in again]
    train, validation, test = first
    assert len(train) > len(validation) and len(train) > len(test)
    assert validation and test


def test_rows_without_a_merchant_are_each_their_own_group():
    anonymous = [{"text": f"dong {i}", "label": "food", "merchant_key": "", "labeled_by": "user"} for i in range(60)]

    train, validation, test = export.split_by_merchant(anonymous)

    assert len(train) + len(validation) + len(test) == 60
    assert validation and test
