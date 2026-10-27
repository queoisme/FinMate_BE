"""Bảng thương hiệu và từ khóa hóa đơn: MỘT nguồn dữ liệu chung với ocr-service (``ocr-service/data/*.json``, lý do từng mục ở ``data/NOTES.md``).

Trước đây ai-service giữ một bản CHÉP trong code (hơn 700 chuỗi) rồi dùng test để canh cho khỏi lệch. Giờ cả hai đọc cùng tệp, nên không có bản chép.
Thư mục dữ liệu: biến môi trường ``LEXICON_DIR`` (image: ``/lexicon``, đưa vào lúc build bằng ``additional_contexts`` của compose, xem Dockerfile);
không đặt thì dùng ``<repo>/ocr-service/data`` khi chạy từ mã nguồn. Thiếu tệp thì báo lỗi to lúc nạp module: chạy với bảng rỗng sẽ phân loại hóa đơn
sai mà không ai biết. Khi có DB, các bảng này được nạp lại từ ``kb_brands``/``kb_keywords`` (xem ``knowledge.py``) và được sửa TẠI CHỖ.
"""
from __future__ import annotations

import json
import os
import re
from pathlib import Path


def _directory() -> Path:
    configured = os.environ.get("LEXICON_DIR")
    return Path(configured) if configured else Path(__file__).resolve().parents[3] / "ocr-service" / "data"


LEXICON_DIR = _directory()


def load(name: str):
    path = LEXICON_DIR / name
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError) as error:
        raise RuntimeError(f"Không đọc được bảng từ vựng {path} ({error}); đặt LEXICON_DIR trỏ tới thư mục chứa {name}") from error


def shared_words(*roles: str) -> tuple[str, ...]:
    """Từ của một hay nhiều VAI TRÒ trong ``shared_words.json`` (từ vựng hóa đơn chung với ocr-service và backend), theo thứ tự trong tệp."""
    data = load("shared_words.json")
    return tuple(word for role in roles for word in data[role]["words"])


def shared_alternation(*roles: str) -> str:
    """Chuỗi ``a|b|c`` (đã escape) cho regex từ các vai trò của ``shared_words``."""
    return "|".join(re.escape(word) for word in shared_words(*roles))


def keyword_table(name: str) -> dict[str, tuple[str, ...]]:
    """Bảng nhóm -> từ khóa. Thứ tự nhóm trong tệp là thứ tự ưu tiên; giá trị là tuple như bản viết cứng trước đây."""
    return {category: tuple(words) for category, words in load(name).items()}
