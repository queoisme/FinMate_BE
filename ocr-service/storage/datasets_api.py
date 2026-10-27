"""Nhập bộ chuẩn (gold) và bill giả qua OCR vào AI Service. (Bản trước kết nối thẳng PostgreSQL: ``datasets_pg.py``.)

- Gold: 30 bill có đáp án đúng (benchmark/expected_databill.json). Bill đã nhập từ chính các ảnh này (nhãn 'imported') chỉ được ĐÁNH DẤU
  is_gold = true (khớp theo tổng tiền + danh mục), không nhập bản mới để khỏi đếm đôi; ảnh còn thiếu thì OCR, lọc riêng tư rồi ghi (labeled_by 'gold').
- Bill giả (dataset_synthetic_ocr.jsonl): ghi với labeled_by 'synthetic_ocr'. Huấn luyện chỉ dùng chúng để học, không đưa vào phần đo.
Chạy lại an toàn: không tạo bản trùng.
"""
from pathlib import Path

from parsing.extract import _alnum, _norm
from storage.ai_api import AiApi
from storage.dataset import Dataset
from storage.storage_api import ApiDataset

SYNTHETIC_LABEL = "synthetic_ocr"


def _expected_name(merchant) -> str:
    """Đáp án tên trong benchmark là mẫu viết liền không dấu (hoặc danh sách mẫu); lưu mẫu đầu tiên, trống nếu không có tên."""
    if isinstance(merchant, list):
        merchant = merchant[0] if merchant else None
    return merchant or ""


def import_gold(api: AiApi, expected: dict, get_lines, user_hash: str = "local", write: bool = False) -> dict:
    """expected: {tên ảnh: {"merchant", "total", "category"}}; get_lines(tên ảnh) -> các dòng chữ đã lọc (chỉ gọi khi cần OCR)."""
    result = {"marked": 0, "added": 0, "already": 0}
    store = ApiDataset(api, user_hash)
    for name, e in expected.items():
        total, category = e["total"], e["category"]
        # đúng một bill nhập sẵn khớp tổng tiền và danh mục: đó chính là bill này (AI Service đánh dấu gold, trừ khi chỉ xem trước)
        claimed = api.claim_gold(total, category, dry_run=not write)
        if claimed == "already":
            result["already"] += 1
        elif claimed == "marked":
            result["marked"] += 1
        else:
            result["added"] += 1
            if write:
                store.append({"merchant": _expected_name(e.get("merchant")), "category": category, "total_amount": total,
                              "label_source": "gold", "is_gold": True, "lines": get_lines(name)})
    return result


_MIN_CONTAINS = 6  # mẫu ngắn hơn chỉ được nối khi trùng hẳn tên, tránh "minh" khớp mọi tên có chữ "minh"


def normalize_gold_names(api: AiApi, brand_display: dict | None = None, write: bool = False) -> list[tuple[str, str]]:
    """Bill gold mới ghi tên theo mẫu viết liền của benchmark ("longchau"); đổi sang tên hiển thị đã có của chính cửa hàng đó
    ("Nhà thuốc Long Châu") lấy từ các bill khác của AI Service, hoặc từ bảng thương hiệu. Không nối được thì giữ nguyên. Trả về [(tên cũ, tên mới)]."""
    names = api.gold_merchant_names()
    known, gold_names = names["known"], names["gold"]
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
                api.rename_gold(old, new)
    return changes


def import_synthetic(api: AiApi, path: Path, user_hash: str = "synthetic", write: bool = False) -> dict:
    rows = Dataset(path).read_all()
    if write:
        store = ApiDataset(api, user_hash)
        for r in rows:
            store.append({**r, "label_source": SYNTHETIC_LABEL})
    return {"in_file": len(rows), "in_db": api.count_labeled_by(SYNTHETIC_LABEL)}
