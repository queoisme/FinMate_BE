"""Từ vựng và ngưỡng của lớp đọc hóa đơn nằm trong data/*.json, không viết cứng trong .py (xem data/NOTES.md).

Lý do: bảng trong code dễ biến thành chuỗi vá theo từng hóa đơn lỗi, và ngưỡng chỉnh theo vài ảnh thì không thấy được là chưa đáng tin. Thêm từ khóa vào
data/parsing_vocab.json, đổi ngưỡng ở data/thresholds.json (kèm số mẫu và nguồn), rồi kiểm trên ảnh CHƯA dùng để chỉnh."""
import ast
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
ALLOWED: set[str] = set()  # 9 nhóm danh mục cũng đã ra data/category_groups.json: không còn ngoại lệ nào


def _module_level_data(path):
    for node in ast.parse(path.read_text(encoding="utf-8")).body:
        if not isinstance(node, ast.Assign) or len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name):
            continue
        name = node.targets[0].id
        if name in ALLOWED or not (name.startswith("_") or name.isupper()):
            continue
        try:
            value = ast.literal_eval(node.value)
        except (ValueError, SyntaxError):
            continue
        if (isinstance(value, (tuple, list, set, dict)) and len(value) >= 3) or (isinstance(value, (int, float)) and not isinstance(value, bool)):
            yield name


def test_parsing_and_reading_modules_define_no_vocabulary_tables_or_thresholds():
    offenders = [f"{path.relative_to(ROOT)}: {name}" for folder in ("parsing", "reading") for path in sorted((ROOT / folder).glob("*.py")) for name in _module_level_data(path)]
    assert not offenders, "chuyển sang data/parsing_vocab.json hoặc data/thresholds.json:\n  " + "\n  ".join(offenders)


def test_every_threshold_records_where_its_value_came_from():
    entries = {k: v for k, v in json.loads((ROOT / "data" / "thresholds.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}
    assert entries
    for key, entry in entries.items():
        assert set(entry) == {"value", "doc", "samples", "source"}, key
        assert entry["samples"] is None or (isinstance(entry["samples"], int) and entry["samples"] >= 1), key
        assert entry["source"], key  # "chưa ghi nguồn đo" cũng là một câu trả lời trung thực; để trống thì không


def test_a_threshold_tuned_on_few_samples_is_flagged_not_hidden():
    # Ngưỡng chỉnh theo dưới 30 mẫu chỉ là điểm xuất phát. Danh sách này phải khớp thực tế để không ai tưởng chúng đã được kiểm chứng rộng.
    entries = json.loads((ROOT / "data" / "thresholds.json").read_text(encoding="utf-8"))
    few = {k for k, v in entries.items() if not k.startswith("_") and v["samples"] is not None and v["samples"] < 30}
    assert {"parsing.extract._MIN_LINE_CONFIDENCE", "reading.quality._DARK_P99", "reading.quality._BRIGHT_P1", "reading.quality._BLUR_VAR"} <= few
