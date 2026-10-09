"""Nhập bộ chuẩn (gold) và bill giả qua OCR vào PostgreSQL.

- Gold: 30 bill có đáp án đúng (benchmark/expected_databill.json). Bill đã nhập từ chính các ảnh này (nhãn 'imported') chỉ được ĐÁNH DẤU
  is_gold = true (khớp theo tổng tiền + danh mục), không nhập bản mới để khỏi đếm đôi; ảnh còn thiếu thì OCR, lọc riêng tư rồi ghi (labeled_by 'gold').
- Bill giả (dataset_synthetic_ocr.jsonl): ghi với labeled_by 'synthetic_ocr'. Huấn luyện chỉ dùng chúng để học, không đưa vào phần đo.
Chạy lại an toàn: không tạo bản trùng.
"""
import json
from pathlib import Path

from dataset import Dataset
from extract import _alnum, _norm
from storage_pg import PgDataset, _cursor

SYNTHETIC_LABEL = "synthetic_ocr"


def _expected_name(merchant) -> str:
    """Đáp án tên trong benchmark là mẫu viết liền không dấu (hoặc danh sách mẫu); lưu mẫu đầu tiên, trống nếu không có tên."""
    if isinstance(merchant, list):
        merchant = merchant[0] if merchant else None
    return merchant or ""


def import_gold(connect, expected: dict, get_lines, user_hash: str = "local", write: bool = False) -> dict:
    """expected: {tên ảnh: {"merchant", "total", "category"}}; get_lines(tên ảnh) -> các dòng chữ đã lọc (chỉ gọi khi cần OCR)."""
    result = {"marked": 0, "added": 0, "already": 0}
    store = PgDataset(connect, user_hash)
    for name, e in expected.items():
        total, category = e["total"], e["category"]
        with _cursor(connect) as cur:
            cur.execute("SELECT id, is_gold FROM bill_labeled_samples WHERE amount_cents = %s AND category_slug = %s AND labeled_by IN ('imported', 'gold')",
                        (total, category))
            rows = cur.fetchall()
        if any(g for _, g in rows):
            result["already"] += 1
            continue
        if len(rows) == 1:  # đúng một bill nhập sẵn khớp tổng tiền và danh mục: đó chính là bill này
            result["marked"] += 1
            if write:
                with _cursor(connect) as cur:
                    cur.execute("UPDATE bill_labeled_samples SET is_gold = TRUE WHERE id = %s", (rows[0][0],))
            continue
        result["added"] += 1
        if write:
            store.append({"merchant": _expected_name(e.get("merchant")), "category": category, "total_amount": total,
                          "label_source": "gold", "is_gold": True, "lines": get_lines(name)})
    return result


_MIN_CONTAINS = 6  # mẫu ngắn hơn chỉ được nối khi trùng hẳn tên, tránh "minh" khớp mọi tên có chữ "minh"


def normalize_gold_names(connect, brand_display: dict | None = None, write: bool = False) -> list[tuple[str, str]]:
    """Bill gold mới ghi tên theo mẫu viết liền của benchmark ("longchau"); đổi sang tên hiển thị đã có của chính cửa hàng đó
    ("Nhà thuốc Long Châu") lấy từ các bill khác trong DB, hoặc từ bảng thương hiệu. Không nối được thì giữ nguyên. Trả về [(tên cũ, tên mới)]."""
    with _cursor(connect) as cur:
        cur.execute("SELECT DISTINCT merchant_name FROM bill_labeled_samples WHERE labeled_by <> 'gold' AND merchant_name IS NOT NULL AND merchant_name <> ''")
        known = [r[0] for r in cur.fetchall()]
        cur.execute("SELECT DISTINCT merchant_name FROM bill_labeled_samples WHERE labeled_by = 'gold' AND merchant_name IS NOT NULL AND merchant_name <> ''")
        gold_names = [r[0] for r in cur.fetchall()]
    candidates = known + list((brand_display or {}).values())
    changes = []
    for old in gold_names:
        key = _alnum(_norm(old))
        if not key:
            continue
        hits = [c for c in candidates if _alnum(_norm(c)) == key or (len(key) >= _MIN_CONTAINS and key in _alnum(_norm(c)))]
        if not hits:
            continue
        exact = [c for c in hits if _alnum(_norm(c)) == key]
        if not exact and len(set(hits)) > 1:
            continue  # mẫu chung ("thucpham") khớp nhiều tên khác nhau: không đoán, giữ nguyên
        new = min(exact or hits, key=len)
        if new != old:
            changes.append((old, new))
            if write:
                with _cursor(connect) as cur:
                    cur.execute("UPDATE bill_raw_samples SET notification_title = %s WHERE source = 'bill_scan' AND notification_title = %s AND id IN "
                                "(SELECT raw_sample_id FROM bill_labeled_samples WHERE labeled_by = 'gold')", (new, old))
                    cur.execute("UPDATE bill_labeled_samples SET merchant_name = %s WHERE labeled_by = 'gold' AND merchant_name = %s", (new, old))
    return changes


def import_synthetic(connect, path: Path, user_hash: str = "synthetic", write: bool = False) -> dict:
    rows = Dataset(path).read_all()
    if write:
        store = PgDataset(connect, user_hash)
        for r in rows:
            store.append({**r, "label_source": SYNTHETIC_LABEL})
    with _cursor(connect) as cur:
        cur.execute("SELECT count(*) FROM bill_labeled_samples WHERE labeled_by = %s", (SYNTHETIC_LABEL,))
        in_db = cur.fetchone()[0]
    return {"in_file": len(rows), "in_db": in_db}
