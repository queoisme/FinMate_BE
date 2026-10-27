"""SQL không phải model ORM của giai đoạn 4 (ERD v5_1): hai view đồng thuận và bộ chặn UPDATE/DELETE trực tiếp của ``ml_label_events``.

Dùng chung cho migration 0024 và test (``create_all`` không tạo view hay trigger), như ``app.db.bill_views``. ``OBJECTS`` là thứ tự tạo; ``DROP_OBJECTS`` là thứ tự bỏ.

Hai view chỉ ĐẾM, không quyết định: ngưỡng đưa một cửa hàng / một món vào kiến thức (đề xuất 3 người và 80% đồng ý) và việc bỏ qua cửa hàng nhiều loại hàng
(``kb_brands.mixed``) do người dùng view quyết định, để đổi ngưỡng không phải đổi schema.
"""

# Mỗi người một phiếu cho mỗi cửa hàng: lấy nhãn MỚI NHẤT của người đó. Chỉ tính bill (``sample_unit = 'bill'``) đã có nhãn cuối.
# agreement = phần phiếu của danh mục này trong tổng số người đã bầu cho cửa hàng (các dòng của một cửa hàng cộng lại bằng 1).
MERCHANT_CONSENSUS_VIEW = """
CREATE OR REPLACE VIEW v_merchant_consensus AS
WITH latest AS (
    SELECT DISTINCT ON (merchant_key, user_id_hash) merchant_key, user_id_hash, final_slug
    FROM ml_training_samples
    WHERE sample_unit = 'bill' AND final_slug IS NOT NULL AND merchant_key IS NOT NULL AND merchant_key <> ''
    ORDER BY merchant_key, user_id_hash, labeled_at DESC NULLS LAST, created_at DESC
), votes AS (
    SELECT merchant_key, final_slug AS category_slug, count(*) AS users
    FROM latest
    GROUP BY merchant_key, final_slug
)
SELECT merchant_key, category_slug, users,
       users::float8 / (sum(users) OVER (PARTITION BY merchant_key))::float8 AS agreement
FROM votes
"""

ITEM_CONSENSUS_VIEW = """
CREATE OR REPLACE VIEW v_item_consensus AS
WITH votes AS (
    SELECT item_key, category_slug, count(*) AS users
    FROM ml_item_feedback
    GROUP BY item_key, category_slug
)
SELECT item_key, category_slug, users,
       users::float8 / (sum(users) OVER (PARTITION BY item_key))::float8 AS agreement
FROM votes
"""

# Chặn UPDATE và DELETE TRỰC TIẾP; cho các thay đổi DÂY CHUYỀN do khóa ngoại sinh ra (xóa mẫu có nhãn, xóa một phản hồi làm ``feedback_id`` về NULL, đổi slug danh mục):
# khi do khóa ngoại kích hoạt thì ``pg_trigger_depth()`` >= 2. Không cho các thay đổi đó thì xóa dữ liệu của người dùng sẽ bị bảng nhật ký này cản.
LABEL_EVENTS_GUARD_FUNCTION = """
CREATE OR REPLACE FUNCTION ml_label_events_append_only() RETURNS trigger AS $$
BEGIN
    IF pg_trigger_depth() >= 2 THEN
        IF TG_OP = 'UPDATE' THEN RETURN NEW; END IF;
        RETURN OLD;
    END IF;
    RAISE EXCEPTION 'ml_label_events chỉ ghi thêm: không được % trực tiếp (hãy thêm sự kiện mới)', TG_OP;
END;
$$ LANGUAGE plpgsql
"""

LABEL_EVENTS_GUARD_TRIGGER = """
CREATE TRIGGER trg_ml_label_events_append_only BEFORE UPDATE OR DELETE ON ml_label_events
FOR EACH ROW EXECUTE FUNCTION ml_label_events_append_only()
"""

OBJECTS = [LABEL_EVENTS_GUARD_FUNCTION, LABEL_EVENTS_GUARD_TRIGGER, MERCHANT_CONSENSUS_VIEW, ITEM_CONSENSUS_VIEW]
DROP_OBJECTS = [
    "DROP VIEW IF EXISTS v_item_consensus",
    "DROP VIEW IF EXISTS v_merchant_consensus",
    "DROP TRIGGER IF EXISTS trg_ml_label_events_append_only ON ml_label_events",
    "DROP FUNCTION IF EXISTS ml_label_events_append_only()",
]
