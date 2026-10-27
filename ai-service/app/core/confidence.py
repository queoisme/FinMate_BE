"""Độ tin cậy của các luật ai-service, đọc từ ``app/data/confidence.json`` (mỗi giá trị kèm lý do). MỘT nguồn thay cho các con số rải trong hàm.

Không phải tham số chỉnh tự do: backend chọn nhánh một chạm / hộp thoại theo độ tin cậy nên các giá trị ràng buộc lẫn nhau và với ngưỡng của backend
(``tests/unit/test_confidence.py`` canh các ràng buộc đó). Thiếu tệp, thiếu khóa hay sai kiểu thì báo lỗi to lúc import: chạy với con số mặc định âm thầm
làm khoản chi rơi sai nhánh mà không ai biết."""
from __future__ import annotations

import json
from pathlib import Path

DATA_PATH = Path(__file__).resolve().parents[1] / "data" / "confidence.json"


def _load(path: Path = DATA_PATH) -> dict[str, float | list[float]]:
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise RuntimeError(f"Không đọc được {path.name}: {error}") from error
    values: dict[str, float | list[float]] = {}
    for name, entry in raw.items():
        if name.startswith("_"):
            continue
        value = entry.get("value") if isinstance(entry, dict) else None
        numbers = value if isinstance(value, list) else [value]
        if not isinstance(entry.get("doc"), str) or not entry["doc"].strip() or not numbers or not all(
                isinstance(n, (int, float)) and not isinstance(n, bool) and 0 <= n <= 1 for n in numbers):
            raise RuntimeError(f"{path.name}: '{name}' phải có 'doc' (lý do) và 'value' là số trong khoảng 0 đến 1 (hoặc mảng số).")
        values[name] = value
    return values


VALUES = _load()


def get(name: str):
    """Giá trị của một độ tin cậy; tên sai thì KeyError ngay lúc import của nơi dùng."""
    return VALUES[name]
