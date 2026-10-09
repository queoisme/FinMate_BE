import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from dataset import Dataset, redact_line


def test_redact_masks_long_digit_runs_but_keeps_amounts():
    assert "0947210344" not in redact_line("SDT 0947210344")
    assert "01826274182239" not in redact_line("C01826274182239CG")
    assert redact_line("TONG 570,500") == "TONG 570,500"
    assert redact_line("Ngay 29/09/2026") == "Ngay 29/09/2026"


def test_append_then_read_all_roundtrip(tmp_path):
    ds = Dataset(tmp_path / "d.jsonl")
    ds.append({"merchant": "Kisnac", "category": "groceries"})
    ds.append({"merchant": "Circle K", "category": "groceries"})
    rows = ds.read_all()
    assert [r["merchant"] for r in rows] == ["Kisnac", "Circle K"]
    assert all("ts" in r for r in rows)


def test_read_all_on_missing_file_is_empty(tmp_path):
    assert Dataset(tmp_path / "none.jsonl").read_all() == []


def test_read_all_accepts_file_starting_with_utf8_bom(tmp_path):
    # PowerShell 5.1 ghi BOM khi sửa file; không được làm rơi bản ghi đầu tiên
    path = tmp_path / "d.jsonl"
    path.write_bytes(b'\xef\xbb\xbf{"merchant":"A"}\n{"merchant":"B"}\n')
    assert [r["merchant"] for r in Dataset(path).read_all()] == ["A", "B"]


def test_read_all_skips_corrupt_lines(tmp_path):
    path = tmp_path / "d.jsonl"
    path.write_text('{"merchant":"A"}\n{broken\n{"merchant":"B"}\n', encoding="utf-8")
    assert [r["merchant"] for r in Dataset(path).read_all()] == ["A", "B"]
