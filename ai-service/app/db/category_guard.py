"""Bộ kiểm LOẠI danh mục ở DB: thay các CHECK liệt kê slug rải ở nhiều bảng bằng MỘT hàm trigger đọc ``kb_categories.kind``.

Trước đây mỗi bảng giữ một CHECK ``category_slug IN (...)`` với danh sách slug viết cứng, nên thêm một danh mục của backend phải sửa cả chục CHECK bằng migration
(quên một bảng là bảng đó từ chối danh mục mới). Giờ thêm danh mục = INSERT một dòng vào ``kb_categories`` với ``kind`` (``expense`` | ``income``; ``legacy`` là dòng bí danh
của 9 nhóm cũ). Mỗi bảng chỉ khai báo LOẠI nào nó nhận:

* ``expense`` — bảng chỉ nhận khoản chi (bộ nhớ tên món, bộ nhớ cửa hàng, nhãn cuối của bill, phiếu theo món);
* ``expense,income`` — bảng nhận mọi slug của backend nhưng không nhận dòng bí danh (nhật ký phân tích, phản hồi người dùng).

Slug chưa có trong ``kb_categories`` để khóa ngoại từ chối; hàm này chỉ chặn sai LOẠI. Dùng chung cho migration 0026 và test (``create_all`` không tạo trigger), như ``app.db.ml_sql``.
Đổi ``kind`` của một danh mục đang có dữ liệu không tự kiểm lại các dòng cũ.
"""

# (bảng, cột, các loại được nhận)
GUARDS: list[tuple[str, str, str]] = [
    ("kb_item_categories", "category_slug", "expense"),
    ("user_merchant_memory", "category_slug", "expense"),
    ("ml_training_samples", "final_slug", "expense"),
    ("ml_item_feedback", "category_slug", "expense"),
    ("ops_analyze_log", "category_slug", "expense,income"),
    ("ml_user_feedback", "predicted_category", "expense,income"),
    ("ml_user_feedback", "corrected_category", "expense,income"),
]

FUNCTION = """
CREATE OR REPLACE FUNCTION kb_require_category_kind() RETURNS trigger AS $$
DECLARE
    v_slug text;
    v_kind text;
BEGIN
    v_slug := to_jsonb(NEW) ->> TG_ARGV[0];
    IF v_slug IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT c.kind INTO v_kind FROM kb_categories c WHERE c.slug = v_slug;
    IF v_kind IS NOT NULL AND NOT (v_kind = ANY (string_to_array(TG_ARGV[1], ','))) THEN
        RAISE EXCEPTION 'danh mục % (loại %) không được dùng ở %.%: chỉ nhận loại %', v_slug, v_kind, TG_TABLE_NAME, TG_ARGV[0], TG_ARGV[1]
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql
"""


def trigger_name(table: str, column: str) -> str:
    return f"trg_{table}_{column}_kind"


def create_trigger(table: str, column: str, kinds: str) -> str:
    return (
        f"CREATE TRIGGER {trigger_name(table, column)} BEFORE INSERT OR UPDATE OF {column} ON {table} "
        f"FOR EACH ROW EXECUTE FUNCTION kb_require_category_kind('{column}', '{kinds}')"
    )


OBJECTS = [FUNCTION, *(create_trigger(*guard) for guard in GUARDS)]
DROP_OBJECTS = [
    *(f"DROP TRIGGER IF EXISTS {trigger_name(table, column)} ON {table}" for table, column, _ in GUARDS),
    "DROP FUNCTION IF EXISTS kb_require_category_kind()",
]
