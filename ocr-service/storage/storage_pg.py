"""Lưu dữ liệu quét bill vào PostgreSQL (DB finmate_ai) thay cho dataset.jsonl và merchant_memory.json.

Bật bằng biến môi trường FINMATE_DATABASE_URL (vd. postgresql://user:pass@host:5434/finmate_ai); không có thì vẫn dùng file như cũ.
FINMATE_USER_HASH: mã băm người dùng (backend truyền vào); mặc định "local" cho máy dev.
Cần: pip install "psycopg[binary]". Bảng: migrations/0006_*.sql (bill dùng lại bill_raw_samples + bill_labeled_samples với source = 'bill_scan').
"""
import hashlib
import os
import uuid
from contextlib import contextmanager
from contextvars import ContextVar
from datetime import datetime, timezone
from pathlib import Path

from storage.dataset import Dataset
from extract import CATEGORIES
from storage.memory import MerchantMemory, _key

SOURCE = "bill_scan"
# ponytail: đối chiếu hai quy ước này với pipeline thông báo ngân hàng của bạn trước khi huấn luyện chung
TRANSACTION_TYPE = "expense"   # chữ thường hay IN HOA tuỳ quy ước hiện có của bill_labeled_samples
AMOUNT_FACTOR = 1              # amount_cents = số đồng * AMOUNT_FACTOR (đặt 100 nếu DB lưu VND x100)


# Người dùng của yêu cầu đang xử lý (backend truyền mã băm trong header X-User-Hash; xem main.bind_user). Không có thì dùng mã mặc định.
current_user: ContextVar[str | None] = ContextVar("finmate_user", default=None)


def _who(default: str) -> str:
    return current_user.get() or default


@contextmanager
def _cursor(connect):
    """Mỗi thao tác một kết nối ngắn: thành công thì commit, lỗi thì rollback. ponytail: thêm connection pool khi tải lớn."""
    conn = connect()
    try:
        with conn:
            yield conn.cursor()
    finally:
        conn.close()


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


class PgMerchantMemory(MerchantMemory):
    """Cùng luật khớp mờ như bản file; chỉ khác chỗ đọc/ghi."""

    def __init__(self, connect, user_hash: str):
        self._connect, self._user = connect, user_hash

    def _load(self) -> dict:
        with _cursor(self._connect) as cur:
            cur.execute("SELECT merchant_key, category_slug, display_name FROM bill_user_merchant_category WHERE user_id_hash = %s", (_who(self._user),))
            rows = cur.fetchall()
        return {k: {"category": c, **({"name": n} if n else {})} for k, c, n in rows}

    def _save(self, key: str, entry: dict, data: dict) -> None:
        now = _now()
        with _cursor(self._connect) as cur:
            cur.execute(
                "INSERT INTO bill_user_merchant_category (id, user_id_hash, merchant_key, display_name, category_slug, created_at, updated_at) "
                "VALUES (%s, %s, %s, %s, %s, %s, %s) "
                "ON CONFLICT (user_id_hash, merchant_key) DO UPDATE SET category_slug = excluded.category_slug, "
                "display_name = COALESCE(excluded.display_name, bill_user_merchant_category.display_name), updated_at = excluded.updated_at",
                (str(uuid.uuid4()), _who(self._user), key, entry.get("name"), entry["category"], now, now))


class PgDataset:
    """Cùng giao diện append/read_all như Dataset (file jsonl)."""

    def __init__(self, connect, user_hash: str):
        self._connect, self._user = connect, user_hash

    def append(self, record: dict) -> None:
        lines = list(record.get("lines") or [])
        merchant, total, category = record.get("merchant") or "", record.get("total_amount"), record["category"]
        if category not in CATEGORIES:
            raise ValueError(f"Danh mục không hợp lệ: {category}")
        content_hash = hashlib.sha256("\n".join([_key(merchant), str(total), *lines]).encode("utf-8")).hexdigest()
        now = _now()
        with _cursor(self._connect) as cur:
            cur.execute(
                "INSERT INTO bill_raw_samples (id, source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash) "
                "VALUES (%s, %s, %s, %s, %s, %s, %s, %s) ON CONFLICT (content_hash) DO NOTHING",
                (str(uuid.uuid4()), SOURCE, SOURCE, merchant, "\n".join(lines), record.get("ts") or now, _who(self._user), content_hash))
            cur.execute("SELECT id FROM bill_raw_samples WHERE content_hash = %s", (content_hash,))
            raw_id = cur.fetchone()[0]
            cur.execute(
                "INSERT INTO bill_labeled_samples (id, raw_sample_id, is_financial, transaction_type, amount_cents, merchant_name, category_slug, labeled_by, labeled_at, is_gold) "
                "VALUES (%s, %s, TRUE, %s, %s, %s, %s, %s, %s, %s) "
                "ON CONFLICT (raw_sample_id) DO UPDATE SET merchant_name = excluded.merchant_name, category_slug = excluded.category_slug, "
                "amount_cents = excluded.amount_cents, labeled_by = excluded.labeled_by, labeled_at = excluded.labeled_at, "
                "is_gold = (bill_labeled_samples.is_gold OR excluded.is_gold)",  # nhãn mới nhất của cùng một bill thắng; đã là gold thì giữ gold
                (str(uuid.uuid4()), raw_id, TRANSACTION_TYPE, None if total is None else total * AMOUNT_FACTOR, merchant, category,
                 record.get("label_source") or "user", now, bool(record.get("is_gold"))))

    def read_all(self) -> list[dict]:
        """Toàn bộ bill đã gán nhãn của MỌI người dùng (dùng cho huấn luyện chung)."""
        with _cursor(self._connect) as cur:
            cur.execute(
                "SELECT r.received_at, r.notification_body, l.merchant_name, l.category_slug, l.labeled_by, l.amount_cents "
                "FROM bill_labeled_samples l JOIN bill_raw_samples r ON r.id = l.raw_sample_id "
                "WHERE r.source = %s AND l.category_slug IS NOT NULL ORDER BY r.received_at", (SOURCE,))
            rows = cur.fetchall()
        return [{"ts": str(ts), "merchant": m, "category": c, "label_source": by,
                 "total_amount": None if cents is None else cents // AMOUNT_FACTOR, "lines": body.split("\n") if body else []}
                for ts, body, m, c, by, cents in rows]


def connect_from_env():
    """Hàm mở kết nối PostgreSQL nếu có FINMATE_DATABASE_URL, không thì None."""
    url = os.environ.get("FINMATE_DATABASE_URL")
    if not url:
        return None
    import psycopg  # chỉ cần khi dùng PostgreSQL
    return lambda: psycopg.connect(url)


def open_stores(base_dir: Path):
    """(bộ nhớ cửa hàng, dataset): PostgreSQL nếu có FINMATE_DATABASE_URL, không thì file cạnh code như trước."""
    connect = connect_from_env()
    if not connect:
        return MerchantMemory(base_dir / "merchant_memory.json"), Dataset(base_dir / "dataset.jsonl")
    user = os.environ.get("FINMATE_USER_HASH", "local")
    return PgMerchantMemory(connect, user), PgDataset(connect, user)
