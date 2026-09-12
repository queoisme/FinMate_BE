"""Fixture dùng chung cho toàn bộ bộ test.

Ở ĐÂY chỉ đặt fixture thuần — không fixture nào được chạm tới Postgres. Mọi thứ cần database
nằm ở ``tests/integration/conftest.py`` để test đơn vị chạy được trên một máy không có
Postgres (bản clone sạch, CI tối giản).
"""

from __future__ import annotations

import json
import os
import pathlib
import uuid


def _load_env_defaults() -> None:
    """Lấy giá trị còn thiếu từ ``.env.example``.

    ``app.core.config`` dựng ``Settings()`` ngay khi import, và nó có những trường bắt buộc
    không mặc định (đúng như vậy — một INTERNAL_API_KEY có giá trị mặc định là một khoá
    mặc định sẽ được deploy). Hệ quả là trên một bản clone sạch hoặc trên CI, pytest hỏng
    ngay ở bước thu thập test chứ không phải ở bước chạy. Bộ test đơn vị không có lý do gì
    phải phụ thuộc vào file .env trên máy của một lập trình viên cụ thể.

    Chỉ điền những biến CHƯA được set, nên môi trường thật luôn thắng.
    """
    example = pathlib.Path(__file__).resolve().parents[1] / ".env.example"
    if not example.exists():
        return
    for line in example.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        os.environ.setdefault(key.strip(), value.strip())


_load_env_defaults()
from datetime import datetime

import pytest

from app.core.anonymizer import sha256_hex
from app.db.seed_patterns import SEED_PATH
from app.utils.provider_patterns import PatternMatcher, PatternSpec
from app.utils.text_utils import VIETNAM_TZ

CORPUS_DIR = pathlib.Path(__file__).resolve().parents[1] / "data" / "corpus"
TEST_DB_NAME = f"finmate_ai_test_{uuid.uuid4().hex[:8]}"


def pattern_rows() -> list[dict]:
    return json.loads(SEED_PATH.read_text(encoding="utf-8"))


@pytest.fixture(scope="session")
def pattern_specs() -> list[PatternSpec]:
    return [
        PatternSpec(**{k: v for k, v in row.items() if k != "notes"})
        for row in pattern_rows()
    ]


@pytest.fixture(scope="session")
def matcher(pattern_specs: list[PatternSpec]) -> PatternMatcher:
    return PatternMatcher(pattern_specs)


@pytest.fixture(scope="session")
def corpus() -> list[dict]:
    rows: list[dict] = []
    for path in sorted(CORPUS_DIR.glob("*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                rows.append(json.loads(line))
    if not rows:
        pytest.fail(f"Corpus rỗng: {CORPUS_DIR}")
    return rows


@pytest.fixture(scope="session")
def now() -> datetime:
    return datetime(2026, 9, 9, 10, 30, tzinfo=VIETNAM_TZ)


@pytest.fixture
def user_hash() -> str:
    return sha256_hex(str(uuid.uuid4()))
