"""Gom phiếu bầu của người dùng thành thương hiệu mới trong bảng thương hiệu của AI Service (nguồn 'vote'). (Bản trước kết nối thẳng PostgreSQL: ``promotion_pg.py``.)

Phiếu lấy từ ``/ocr-store/consensus/merchants`` (view ``v_merchant_consensus``): nhãn cuối của các bill mà người dùng ĐỒNG Ý đóng góp, mỗi người một phiếu cho mỗi cửa hàng. Cố ý KHÔNG đọc
bộ nhớ riêng của từng người (``user_merchant_memory``, chưa ai đồng ý chia sẻ). Phiếu là slug backend, đổi về 9 nhóm của ocr-service bằng ``GROUP_OF`` (slug không có nhóm thì bỏ qua).
Một cửa hàng chỉ được thêm vào bảng thương hiệu CHUNG khi:
  - đủ MIN_USERS người KHÁC NHAU cùng xếp vào một danh mục, và
  - người thắng chiếm >= MIN_AGREEMENT tổng số phiếu  -> bật ngay (is_active = true)
Có từ MIN_CANDIDATE_USERS người nhưng chưa đủ ngưỡng -> ghi ở trạng thái chờ (is_active = false) cho người duyệt.
Chỉ một người chốt thì giữ riêng cho người đó, không đưa vào bảng chung. Không bao giờ ghi đè dòng đã có do người khác thêm.
"""
from parsing import extract
from parsing.extract import _alnum, _brand_in, _looks_like_code_or_status
from storage.ai_api import AiApi
from storage.storage_api import GROUP_OF

MIN_USERS = 3
MIN_AGREEMENT = 0.8
MIN_CANDIDATE_USERS = 2
MIN_KEY_LEN = 5      # khóa ngắn hơn (bỏ dấu cách) khớp nhầm giữa chữ khác; để người duyệt tự quyết
WHOLE_WORD_MAX = 6   # khóa ngắn vừa phải chỉ khớp khi là cả từ


def promote(api: AiApi, write: bool = False) -> dict:
    """Trả về {"promoted", "pending", "upgraded", "disputes", "skipped"} (danh sách khóa cửa hàng); write=False thì chỉ tính, không ghi."""
    votes = api.merchant_consensus()
    existing = {b["brand_key"]: (b["category_slug"], bool(b["is_active"]), b["source"]) for b in api.knowledge(include_inactive=True)["brands"]}
    by_merchant: dict[str, dict[str, tuple[int, str | None]]] = {}
    for vote in votes:
        if vote["category_slug"] in GROUP_OF:  # tập phiếu không có tên đẹp: dòng thương hiệu mới dùng key.title()
            by_merchant.setdefault(vote["merchant_key"], {})[GROUP_OF[vote["category_slug"]]] = (vote["users"], None)
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
                    api.activate_voted_brand(key)
            continue
        if any(_brand_in(b, key) for b in existing):
            out["skipped"].append(key)  # đã được một thương hiệu có sẵn bao phủ ("Highlands Coffee Quận 9")
            continue
        if not solid and total < MIN_CANDIDATE_USERS:
            continue  # chỉ một người chốt: giữ riêng
        out["promoted" if solid else "pending"].append(key)
        if write:
            api.propose_brand({"brand_key": key, "category_slug": winner, "display_name": name or key.title(),
                               "whole_word": len(_alnum(key)) <= WHOLE_WORD_MAX, "is_active": solid})
    return out
