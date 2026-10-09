"""SQL của hai view ``bill_*`` dùng chung cho migration 0006 và test (view không phải model ORM nên ``create_all`` không tạo chúng)."""

# Hàng đợi duyệt: cửa hàng lạ nào được quét nhiều nhất / bởi nhiều người nhất.
UNKNOWN_MERCHANTS_VIEW = """
CREATE OR REPLACE VIEW bill_unknown_merchants AS
SELECT merchant_key,
       max(merchant_read)                                AS sample_name,
       count(*)                                          AS bills,
       sum(seen_count)                                   AS scans,
       count(DISTINCT user_id_hash)                      AS users,
       mode() WITHIN GROUP (ORDER BY predicted_category) AS predicted_category
FROM bill_unknown_scans
WHERE status = 'new' AND merchant_key <> ''
GROUP BY merchant_key
"""

# Phiếu bầu: bao nhiêu người cùng xếp một cửa hàng vào một danh mục.
MERCHANT_VOTES_VIEW = """
CREATE OR REPLACE VIEW bill_merchant_votes AS
SELECT merchant_key,
       category_slug,
       count(DISTINCT user_id_hash) AS users,
       max(display_name)            AS sample_name
FROM bill_user_merchant_category
GROUP BY merchant_key, category_slug
"""
