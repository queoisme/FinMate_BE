"""Dataset hóa đơn đã có nhãn (JSON Lines) để huấn luyện mô hình sau này."""
import json
import re
import threading
from datetime import datetime, timezone
from pathlib import Path

_LONG_DIGITS = re.compile(r"\d{9,}")  # số điện thoại, mã hóa đơn, số thẻ...


def redact_line(text: str) -> str:
    """Che chuỗi từ 9 chữ số liền nhau; giữ nguyên số tiền (có dấu , .) và ngày tháng."""
    return _LONG_DIGITS.sub("[so-dai]", text)


class Dataset:
    def __init__(self, path: Path):
        self._path = Path(path)
        self._lock = threading.Lock()

    def append(self, record: dict) -> None:
        row = {"ts": datetime.now(timezone.utc).isoformat(timespec="seconds"), **record}
        with self._lock, self._path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")

    def read_all(self) -> list[dict]:
        try:
            lines = self._path.read_text(encoding="utf-8-sig").splitlines()  # chịu được BOM
        except OSError:
            return []
        rows = []
        for line in lines:
            try:
                rows.append(json.loads(line))
            except ValueError:
                continue  # bỏ qua dòng hỏng, không làm mất cả dataset
        return rows
