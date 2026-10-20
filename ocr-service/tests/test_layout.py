"""Bố cục thư mục: module đã chuyển vào package vẫn tìm đúng thư mục dữ liệu ở gốc dịch vụ (đường dẫn tính theo vị trí file không được lệch)."""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from reading import vietocr_onnx


def test_vietocr_models_live_at_the_service_root_not_inside_the_package():
    assert vietocr_onnx._DIR == ROOT / "models" / "vietocr"
    assert not (ROOT / "reading" / "models").exists()


def test_the_service_entry_point_stays_at_the_root():
    # Dockerfile chạy `uvicorn main:app` từ thư mục dịch vụ.
    assert (ROOT / "main.py").is_file()
