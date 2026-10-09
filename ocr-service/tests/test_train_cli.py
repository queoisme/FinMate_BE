import json
import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from test_category_model import _rows
from train_category_model import run


def _write(path, rows):
    path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows), encoding="utf-8")


def test_run_saves_model_when_gate_passes(tmp_path, capsys):
    data, model = tmp_path / "d.jsonl", tmp_path / "m.joblib"
    _write(data, _rows())
    assert run(data, model) == 0
    assert model.exists()
    assert "Đạt" in capsys.readouterr().out


def test_run_does_not_create_model_when_data_is_insufficient(tmp_path, capsys):
    data, model = tmp_path / "d.jsonl", tmp_path / "m.joblib"
    _write(data, _rows(per_class_merchants=1))
    assert run(data, model) == 1
    assert not model.exists()
    assert "30" in capsys.readouterr().out
