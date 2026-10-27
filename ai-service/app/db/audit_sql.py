"""Trigger chỉ-ghi-thêm cho ``kb_audit_log`` (migration 0027).

Dùng chung cho migration và test (``create_all`` không tạo trigger), như ``app.db.ml_sql``. ``OBJECTS`` là thứ tự tạo; ``DROP_OBJECTS`` là thứ tự bỏ.

Nhật ký quản trị chỉ có ý nghĩa khi không ai sửa được nó: hoàn tác (``revert``) cũng là MỘT DÒNG MỚI, không bao giờ sửa dòng cũ. Bảng không có khóa ngoại nào
nên không có thay đổi dây chuyền cần cho qua (khác ``ml_label_events``). ``TRUNCATE`` cố ý KHÔNG bị chặn: test dọn bảng bằng nó, và trigger mức dòng không bắt được nó.
Superuser/chủ bảng vẫn ``ALTER TABLE ... DISABLE TRIGGER`` được; muốn chặn tuyệt đối phải thu quyền UPDATE/DELETE của vai trò ứng dụng.
"""

GUARD_FUNCTION = """
CREATE OR REPLACE FUNCTION kb_audit_log_append_only() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'kb_audit_log chỉ được thêm dòng, không được % (nhật ký quản trị)', lower(TG_OP)
        USING ERRCODE = 'integrity_constraint_violation';
END;
$$ LANGUAGE plpgsql
"""

GUARD_TRIGGER = """
CREATE TRIGGER trg_kb_audit_log_append_only BEFORE UPDATE OR DELETE ON kb_audit_log
FOR EACH ROW EXECUTE FUNCTION kb_audit_log_append_only()
"""

OBJECTS = [GUARD_FUNCTION, GUARD_TRIGGER]
DROP_OBJECTS = [
    "DROP TRIGGER IF EXISTS trg_kb_audit_log_append_only ON kb_audit_log",
    "DROP FUNCTION IF EXISTS kb_audit_log_append_only()",
]
