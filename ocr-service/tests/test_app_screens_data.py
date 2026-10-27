"""data/app_screens.json: chữ và hằng số theo giao diện từng app (parsing/app_screens.py đọc lúc dùng). Test canh dạng dữ liệu để một lần sửa sai không làm
app_screens chết âm thầm, và canh việc mã không viết lại các cụm chữ đó."""
import ast
import json
import pathlib
import re
import unicodedata

from classification import data_files
from parsing import app_screens

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = {k: v for k, v in json.loads((ROOT / "data" / "app_screens.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}
GROUPS = set(data_files.load("category_groups.json")["groups"])
SCREENS = ("order_list", "ride", "bill_category", "service_payment", "cinema_ticket", "shipping_label", "wallet_transaction", "momo_result", "provider_notice",
           "food_delivery", "bus_ticket", "paper_bus_ticket")


def fold(text: str) -> str:
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def _leaves(node, path=()):
    if isinstance(node, dict):
        for key, value in node.items():
            yield from _leaves(value, path + (key,))
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from _leaves(value, path + (index,))
    else:
        yield path, node


def _key(path):
    """Tên khóa gần nhất (bỏ chỉ số mảng)."""
    return next(part for part in reversed(path) if isinstance(part, str))


def test_every_screen_the_code_reads_is_described_and_documented():
    assert set(SCREENS) <= set(SPEC)
    for name, entry in SPEC.items():
        assert entry["doc"].strip(), name


def test_phrases_are_lowercase_and_unaccented_because_the_code_matches_on_folded_lines():
    suffixes = ("_phrase", "_prefix", "_label", "_phrases", "_prefixes", "_tokens", "_words", "skip_lines", "match", "_word")
    for path, value in _leaves(SPEC):
        key = _key(path)
        if isinstance(value, str) and key.endswith(suffixes) and key != "doc":
            assert value == value.lower() and value == fold(value), f"{'/'.join(map(str, path))}: '{value}' phải viết thường, bỏ dấu"


def test_every_regex_compiles_and_has_the_group_the_code_expects():
    for path, value in _leaves(SPEC):
        key = _key(path)
        if key.endswith("_regex") and isinstance(value, str):
            compiled = re.compile(value)
            if key in {"total_regex", "dish_total_regex", "paid_regex", "merchant_regex"}:
                assert compiled.groups >= 1, f"{'/'.join(map(str, path))} phải có nhóm bắt giá trị"


def test_categories_are_among_the_nine_bill_groups_and_numbers_are_sane():
    for path, value in _leaves(SPEC):
        key = _key(path)
        if key in {"category", "housing_category", "default_category"}:
            assert value in GROUPS, f"{'/'.join(map(str, path))}: '{value}' không thuộc 9 nhóm hóa đơn"
        if key.endswith(("_span", "_window", "_window_lines")):
            assert isinstance(value, int) and 1 <= value <= 30, f"{'/'.join(map(str, path))}: số dòng phải là số nguyên nhỏ"
    for name, low, high in (("cinema_ticket", "amount_min", "amount_max"), ("paper_bus_ticket", "price_min", "price_max")):
        assert 0 < SPEC[name][low] < SPEC[name][high]


def test_every_shipped_value_is_actually_read_by_the_code():
    # một khóa không ai đọc là số chết: người sửa nó tưởng đã đổi hành vi
    source = (ROOT / "parsing" / "app_screens.py").read_text(encoding="utf-8")
    for screen, entry in SPEC.items():
        for key, value in entry.items():
            if key == "doc":
                continue
            assert f'"{key}"' in source, f"{screen}.{key} không được app_screens.py đọc"
            if isinstance(value, dict):
                for inner in value:
                    assert f'"{inner}"' in source, f"{screen}.{key}.{inner} không được app_screens.py đọc"


def _strings(path: pathlib.Path) -> list[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    docstrings = {id(node.body[0].value) for node in ast.walk(tree) if isinstance(node, (ast.Module, ast.FunctionDef, ast.ClassDef)) and node.body
                  and isinstance(node.body[0], ast.Expr) and isinstance(node.body[0].value, ast.Constant)}
    return [node.value for node in ast.walk(tree) if isinstance(node, ast.Constant) and isinstance(node.value, str) and id(node) not in docstrings]


def test_the_interface_phrases_are_not_written_again_in_the_code():
    phrases = {value for path, value in _leaves(SPEC) if isinstance(value, str) and value == fold(value).lower() and not _key(path).endswith("regex")
               and _key(path) not in {"doc", "name", "merchant", "platform_name", "category", "housing_category", "default_category", "currency_word"} and len(value) >= 4}
    offenders = sorted(phrases & set(_strings(ROOT / "parsing" / "app_screens.py")))
    assert not offenders, f"app_screens.py: {offenders} phải đọc từ data/app_screens.json, không viết lại"


def test_the_module_reads_its_spec_late_so_a_replaced_spec_takes_effect(monkeypatch):
    patched = json.loads(json.dumps(app_screens._SPEC))
    patched["ride"]["anchor_phrase"] = "zz ma chuyen di moi"
    ride = ["16:55", "Mã chuyến đi", "Total", "32.000đ"]
    assert app_screens.parse(ride)["screen_type"] == "ride"
    monkeypatch.setattr(app_screens, "_SPEC", patched)
    assert app_screens.parse(ride) is None  # nhãn cũ không còn được nhận
    assert app_screens.parse(["zz Mã chuyến đi mới", "Total", "32.000đ"])["screen_type"] == "ride"
