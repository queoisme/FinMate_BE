"""Gom phiếu bầu của người dùng thành thương hiệu mới trong bill_brands (nguồn 'vote').

Mỗi người dùng tự chốt "cửa hàng -> danh mục" (bill_user_merchant_category). Một cửa hàng chỉ được thêm vào bảng thương hiệu CHUNG khi:
  - đủ MIN_USERS người KHÁC NHAU cùng xếp vào một danh mục, và
  - người thắng chiếm >= MIN_AGREEMENT tổng số phiếu  -> bật ngay (is_active = true)
Có từ MIN_CANDIDATE_USERS người nhưng chưa đủ ngưỡng -> ghi ở trạng thái chờ (is_active = false) cho người duyệt.
Chỉ một người chốt thì giữ riêng cho người đó, không đưa vào bảng chung. Không bao giờ ghi đè dòng đã có do người khác thêm.
"""
import uuid
from datetime import datetime, timezone

from parsing import extract
from storage import knowledge_pg
from parsing.extract import _alnum, _brand_in, _looks_like_code_or_status
from storage.storage_pg import _cursor

MIN_USERS = 3
MIN_AGREEMENT = 0.8
MIN_CANDIDATE_USERS = 2
MIN_KEY_LEN = 5      # khóa ngắn hơn (bỏ dấu cách) khớp nhầm giữa chữ khác; để người duyệt tự quyết
WHOLE_WORD_MAX = 6   # khóa ngắn vừa phải chỉ khớp khi là cả từ


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def promote(connect, write: bool = False) -> dict:
    """Trả về {"promoted", "pending", "upgraded", "disputes", "skipped"} (danh sách khóa cửa hàng); write=False thì chỉ tính, không ghi."""
    with _cursor(connect) as cur:
        cur.execute("SELECT merchant_key, category_slug, COUNT(DISTINCT user_id_hash), MAX(display_name) FROM bill_user_merchant_category "
                    "GROUP BY merchant_key, category_slug")
        votes = cur.fetchall()
        cur.execute("SELECT brand_key, category_slug, is_active, source FROM bill_brands")
        existing = {k: (c, bool(a), s) for k, c, a, s in cur.fetchall()}
    by_merchant: dict[str, dict[str, tuple[int, str | None]]] = {}
    for key, category, users, name in votes:
        by_merchant.setdefault(key, {})[category] = (users, name)
    out = {"promoted": [], "pending": [], "upgraded": [], "disputes": [], "skipped": []}
    for key, per_category in sorted(by_merchant.items()):
        total = sum(u for u, _ in per_category.values())
        winner, (users, name) = max(per_category.items(), key=lambda kv: kv[1][0])
        if len(_alnum(key)) < MIN_KEY_LEN or _looks_like_code_or_status(key) or winner not in extract.CATEGORIES:
            out["skipped"].append(key)
            continue
        solid = users >= MIN_USERS and users / total >= MIN_AGREEMENT
        if key in existing:
            category, active, source = existing[key]
            if category != winner and solid:
                out["disputes"].append(key)  # đa số người dùng không đồng ý với dòng đã có: để người duyệt quyết, không tự ghi đè
            elif category == winner and source == "vote" and not active and solid:
                out["upgraded"].append(key)
                if write:
                    with _cursor(connect) as cur:
                        cur.execute("UPDATE bill_brands SET is_active = TRUE, updated_at = %s WHERE brand_key = %s AND source = 'vote' AND is_active = FALSE", (_now(), key))
            continue
        if any(_brand_in(b, key) for b in existing):
            out["skipped"].append(key)  # đã được một thương hiệu có sẵn bao phủ ("Highlands Coffee Quận 9")
            continue
        if not solid and total < MIN_CANDIDATE_USERS:
            continue  # chỉ một người chốt: giữ riêng
        out["promoted" if solid else "pending"].append(key)
        if write:
            with _cursor(connect) as cur:
                cur.execute(
                    "INSERT INTO bill_brands (id, brand_key, category_slug, display_name, whole_word, is_active, source, created_at, updated_at) "
                    "VALUES (%s, %s, %s, %s, %s, %s, 'vote', %s, %s) ON CONFLICT (brand_key) DO NOTHING",
                    (str(uuid.uuid4()), key, winner, name or key.title(), len(_alnum(key)) <= WHOLE_WORD_MAX, solid, _now(), _now()))
    return out
