import sys, pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

import pytest

import main
from dataset import Dataset
from memory import MerchantMemory


@pytest.fixture(autouse=True)
def isolated_storage(tmp_path, monkeypatch):
    """Mọi test dùng file tạm, không bao giờ ghi vào dataset.jsonl / merchant_memory.json thật."""
    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "memory.json"))
    monkeypatch.setattr(main, "dataset", Dataset(tmp_path / "dataset.jsonl"))


@pytest.fixture(autouse=True)
def internal_key_ok():
    """Các test cũ không gửi khóa nội bộ: bỏ qua bước xác thực. tests/test_internal_key.py tự gỡ phần này để thử xác thực thật."""
    main.app.dependency_overrides[main.verify_internal_api_key] = lambda: None
    yield
    main.app.dependency_overrides.pop(main.verify_internal_api_key, None)


def boxes_patch(read_lines_fn):
    """Bọc hàm đọc chữ giả (trả [(text, conf)]) thành hàm thay cho main.read_boxes: xếp chồng các dòng thành hộp."""
    from ocr import OcrBox

    def fake(data):
        return [OcrBox(t, c, 0.0, i * 20.0, 100.0, i * 20.0 + 18.0) for i, (t, c) in enumerate(read_lines_fn(data))]

    return fake
