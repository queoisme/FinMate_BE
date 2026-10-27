"""Bảng ``bill_*``: kiến thức nhận diện và dữ liệu huấn luyện của luồng quét hóa đơn.

Luồng hóa đơn dùng 9 nhóm của bộ luật bill (``BILL_CATEGORY_GROUPS``), nay là các dòng trong ``kb_categories`` (dòng chính hoặc bí danh).
Dữ liệu huấn luyện và lịch sử model của hóa đơn đã GỘP vào bộ ``ml_*`` chung (migration 0023): mẫu thô ``source = 'bill_scan'`` ở ``ml_raw_samples``,
nhãn ở ``ml_labeled_samples``, model ở ``ml_models`` (``bill_slug``, ``bill_category``), phiên bản/lần huấn luyện/đánh giá ở ``ml_model_versions``,
``ml_training_jobs``, ``ml_evaluation_*``.

Nội dung còn lại ở đây:
  - kiến thức: ``kb_brands`` (thương hiệu -> nhóm), ``kb_keywords`` (từ khóa theo cửa hàng / mặt hàng / chat), ``user_merchant_memory`` (cửa hàng người dùng đã chốt, theo ``user_id_hash``);
  - ``bill_unknown_scans``: bill lạ chờ người duyệt. GIỮ có chủ đích, là bảng DUY NHẤT ngoài ERD v5_1: ocr-service ghi vào đó và nó là hàng đợi duyệt cửa hàng lạ (xem ``scripts/purge_unknown_scans.py``).

Bảng ``bill_user_merchant_category`` (9 nhóm cũ) và view ``bill_merchant_votes`` đã bị gộp vào ``user_merchant_memory`` ở migration 0029. View ``bill_unknown_merchants`` chỉ nằm trong
migration (view không phải model ORM).

Tên bảng đã đổi sang tiền tố ERD v5_1 (migration 0021) cho các bảng không bị gộp: ``bill_brands`` -> ``kb_brands``, ``bill_keywords`` -> ``kb_keywords``,
``bill_item_cache`` -> ``kb_item_categories``, ``bill_ai_instructions`` -> ``kb_ai_instructions``, ``bill_rule_audit`` -> ``kb_audit_log``,
``bill_user_merchant_choice`` -> ``user_merchant_memory``, ``bill_training_samples`` -> ``ml_training_samples``, ``bill_classify_stats`` -> ``ops_usage_daily``.
Tên lớp ORM và tên chỉ mục/ràng buộc cũ giữ nguyên (đổi tên bảng trong Postgres không đổi chúng).
Các CHECK liệt kê slug (``category_slug IN (...)``) đã được thay bằng khóa ngoại ``kb_categories`` + trigger kiểm loại danh mục (``app.db.category_guard``, migration 0026).
"""

from datetime import date

from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    Date,
    Float,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import Mapped, mapped_column

from app.bill import lexicon
from app.core.enums import CATEGORY_SLUGS
from app.db.base import Base, created_at, long_text, timestamptz, uuid_fk, uuid_pk
from app.db.models.category import category_fk

# Đổi tập nhóm trong category_groups.json thì cần migration mới cho các ràng buộc CHECK dùng tuple này (migration cũ giữ nguyên).
BILL_CATEGORY_GROUPS = tuple(lexicon.load("category_groups.json")["groups"])
BILL_KEYWORD_KINDS = ("merchant", "product")  # từ điển chat không nằm đây: tài liệu chat_dictionary (migration 0016)
# Danh mục người dùng chọn khi lưu một khoản chi từ hóa đơn: slug của backend, trừ "income" (hóa đơn là tiền ra).
BILL_CHOICE_SLUGS = tuple(slug for slug in CATEGORY_SLUGS if slug != "income")
# Giá trị thật mà rules_admin._log ghi vào nhật ký. ERD v5_1 ghi 'item', code đang ghi 'item_cache' (giữ theo code); 'provider_format' dành cho bộ định dạng sắp làm.
AUDIT_RESOURCES = ("brand", "keyword", "item_cache", "instruction", "provider_format", "category")
AUDIT_ACTIONS = ("create", "update", "activate", "deactivate", "delete", "revert")


def _in(column: str, values: tuple[str, ...]) -> str:
    return f"{column} IN ({', '.join(repr(v) for v in values)})"


class BillUserMerchantChoice(Base):
    """Cửa hàng người dùng đã chốt danh mục KHI LƯU GIAO DỊCH, theo ``user_id_hash``. Lưu đúng slug backend (11 slug) người dùng đã chọn.

    Người dùng chỉ chọn được trong danh mục của backend, nên nhớ nguyên slug đó là không mất gì, còn đổi ngược về 9 nhóm thì mất (food là Ăn uống hay Siêu thị?).
    Bảng cũ theo 9 nhóm (``bill_user_merchant_category``) đã được gộp vào đây ở migration 0029.
    """

    __tablename__ = "user_merchant_memory"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_key: Mapped[str] = mapped_column(Text, nullable=False)  # tên bỏ dấu, chữ thường, gọn khoảng trắng
    display_name: Mapped[str | None] = mapped_column(Text)
    category_slug: Mapped[str] = mapped_column(String(32), category_fk(), nullable=False)
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("user_id_hash", "merchant_key", name="uq_bill_user_merchant_choice"),
        CheckConstraint("merchant_key = lower(merchant_key)", name="chk_user_merchant_memory_key_lower"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_user_merchant_memory_user_hash"),
    )


class BillItemCache(Base):
    """Tên món (đã bỏ dấu, bỏ số tiền) -> slug mà Gemini đã trả lời. Dùng chung mọi người dùng, KHÔNG có cột người dùng.

    Chỉ ghi tên giống tên hàng thật (xem ``item_classifier._rememberable``) và slug hợp lệ; câu trả lời đầu tiên thắng.
    """

    __tablename__ = "kb_item_categories"

    item_key: Mapped[str] = mapped_column(Text, primary_key=True)
    category_slug: Mapped[str] = mapped_column(String(32), category_fk(), nullable=False)
    hits: Mapped[int] = mapped_column(nullable=False, server_default=text("1"))
    source: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'ai'"))  # ai | admin | consensus
    ai_model: Mapped[str | None] = mapped_column(String(64))  # model đã trả lời khi source = 'ai'
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        CheckConstraint("hits >= 1", name="chk_kb_item_categories_hits"),
        CheckConstraint("source IN ('ai', 'admin', 'consensus')", name="chk_kb_item_categories_source"),
        CheckConstraint("char_length(item_key) BETWEEN 3 AND 60", name="chk_kb_item_categories_key_len"),
        # Màn hình quản trị sắp xếp theo hits giảm dần: Postgres quét ngược chỉ mục tăng dần được, không cần khai báo DESC.
        Index("idx_kb_item_categories_hits", "hits"),
    )


class BillTrainingSample(Base):
    """Chữ ĐÃ lọc của một lần quét + nhãn cuối do NGƯỜI DÙNG chọn, chỉ khi họ đồng ý đóng góp dữ liệu. Không ảnh, không chữ gốc.

    ``scan_id`` là id bản ghi quét ở backend (một lần quét = một hàng). Hàng chưa gắn nhãn bị dọn sau 7 ngày (``app.bill.samples.purge_pending``).
    ``user_id_hash`` là SHA-256 một chiều của người dùng; xóa theo người dùng dùng đúng cột này.
    """

    __tablename__ = "ml_training_samples"

    scan_id: Mapped[uuid_fk] = mapped_column(UUID(as_uuid=True), primary_key=True)
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_key: Mapped[str | None] = mapped_column(Text)
    lines: Mapped[str] = mapped_column(Text, nullable=False)  # các dòng đã lọc nối bằng xuống dòng
    predicted_slug: Mapped[str | None] = mapped_column(String(32), category_fk())
    predicted_source: Mapped[str | None] = mapped_column(String(16))
    final_slug: Mapped[str | None] = mapped_column(String(32), category_fk())
    sample_unit: Mapped[str] = mapped_column(String(8), nullable=False, server_default=text("'bill'"))  # bill = cả hóa đơn một nhóm | item = một món
    created_at: Mapped[created_at]
    labeled_at: Mapped[timestamptz | None]

    __table_args__ = (
        CheckConstraint("sample_unit IN ('bill', 'item')", name="chk_ml_training_samples_unit"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_ml_training_samples_user_hash"),
        Index("ix_bill_training_samples_user", "user_id_hash"),
        Index("ix_bill_training_samples_pending", "created_at", postgresql_where=text("final_slug IS NULL")),
        # Cho view v_merchant_consensus: gom nhãn cuối theo (cửa hàng, danh mục).
        Index("idx_ml_training_samples_merchant_slug", "merchant_key", "final_slug", postgresql_where=text("final_slug IS NOT NULL")),
    )


class BillClassifyStat(Base):
    """Bộ đếm theo ngày của ``/bill/classify-items``: bao nhiêu món được trả lời bởi bộ nhớ, luật, Gemini... CHỈ số đếm, không tên món, cửa hàng hay người dùng."""

    __tablename__ = "ops_usage_daily"

    day: Mapped[date] = mapped_column(Date, primary_key=True)
    metric: Mapped[str] = mapped_column(String(32), primary_key=True)
    value: Mapped[int] = mapped_column(BigInteger, nullable=False, server_default=text("0"))

    __table_args__ = (
        CheckConstraint("value >= 0", name="chk_ops_usage_daily_value"),
        CheckConstraint("metric ~ '^[a-z0-9_]+$'", name="chk_ops_usage_daily_metric"),
    )


class BillBrand(Base):
    __tablename__ = "kb_brands"

    id: Mapped[uuid_pk]
    brand_key: Mapped[str] = mapped_column(Text, nullable=False)  # tên bỏ dấu, chữ thường
    category_slug: Mapped[str] = mapped_column(String(32), category_fk(), nullable=False)
    display_name: Mapped[str] = mapped_column(Text, nullable=False)
    whole_word: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("false"))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    # Thương hiệu bán nhiều loại hàng (siêu thị, tạp hóa): đặt bởi API quản trị, có ghi nhật ký.
    mixed: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("false"))
    source: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'seed'"))
    created_at: Mapped[created_at]
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("brand_key", name="uq_bill_brands_brand_key"),
        CheckConstraint("brand_key = lower(brand_key)", name="chk_kb_brands_key_lower"),
        Index("idx_kb_brands_category", "category_slug"),
    )


class BillKeyword(Base):
    __tablename__ = "kb_keywords"

    id: Mapped[uuid_pk]
    kind: Mapped[str] = mapped_column(String(16), nullable=False)
    category_slug: Mapped[str] = mapped_column(String(32), category_fk(), nullable=False)
    keyword: Mapped[str] = mapped_column(Text, nullable=False)  # đã bỏ dấu, chữ thường
    # Nhóm có số nhỏ hơn được xét trước khi một tên trùng nhiều nhóm (giữ đúng thứ tự ưu tiên trong code).
    category_priority: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("100"))
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    source: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'seed'"))
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("kind", "category_slug", "keyword", name="uq_bill_keyword"),
        CheckConstraint(_in("kind", BILL_KEYWORD_KINDS), name="chk_bill_keywords_kind"),
        CheckConstraint("category_priority >= 0", name="chk_kb_keywords_priority"),
        # Truy vấn nạp kiến thức: chỉ từ khóa đang bật, theo thứ tự ưu tiên.
        Index("idx_kb_keywords_active_load", "category_priority", "category_slug", "keyword", postgresql_where=text("is_active")),
    )


class BillAiInstruction(Base):
    """Luật phân loại từng món cho Gemini (nhóm, luật chốt khi nhập nhằng, ví dụ). Sửa ở DB, không cần build lại.

    Đúng MỘT dòng ``is_active`` (partial unique index); phần nhiệm vụ/an toàn và schema kết quả do code giữ, không nằm ở đây.
    """

    __tablename__ = "kb_ai_instructions"

    id: Mapped[uuid_pk]
    instruction_text: Mapped[str] = mapped_column(Text, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("true"))
    updated_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        Index("uq_bill_ai_instructions_one_active", "is_active", unique=True, postgresql_where=text("is_active")),
        CheckConstraint("char_length(instruction_text) > 0", name="chk_kb_ai_instructions_text"),
    )


class BillRuleAudit(Base):
    """Nhật ký mọi thay đổi luật qua API quản trị: trước/sau của dòng bị đổi, ai đổi (``actor_hash`` = SHA-256 id admin, không phải id thật), vì sao.
    Là nguồn cho "hoàn tác": mỗi dòng đủ để đưa luật về trạng thái trước đó."""

    __tablename__ = "kb_audit_log"

    id: Mapped[uuid_pk]
    resource: Mapped[str] = mapped_column(String(16), nullable=False)  # brand | keyword | item_cache | instruction
    resource_key: Mapped[str] = mapped_column(Text, nullable=False)  # brand_key | id của từ khóa | item_key | id của chỉ dẫn
    action: Mapped[str] = mapped_column(String(12), nullable=False)  # create | update | activate | deactivate | delete | revert
    before: Mapped[dict | None] = mapped_column(JSONB)
    after: Mapped[dict | None] = mapped_column(JSONB)
    actor_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    reason: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[created_at]

    __table_args__ = (
        Index("idx_bill_rule_audit_created", "created_at"),
        Index("idx_bill_rule_audit_resource", "resource", "resource_key"),
        CheckConstraint(_in("resource", AUDIT_RESOURCES), name="chk_kb_audit_log_resource"),
        CheckConstraint(_in("action", AUDIT_ACTIONS), name="chk_kb_audit_log_action"),
        CheckConstraint("actor_hash ~ '^[0-9a-f]{64}$'", name="chk_kb_audit_log_actor_hash"),
    )


class BillUnknownScan(Base):
    """Bill lạ: chưa nhận ra cửa hàng. Chỉ ghi khi người dùng đồng ý chia sẻ; chữ đã lọc, không ảnh. Quá hạn (``last_seen_at``) bị ``scripts/purge_unknown_scans.py`` dọn."""

    __tablename__ = "bill_unknown_scans"

    id: Mapped[uuid_pk]
    user_id_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    merchant_read: Mapped[str | None] = mapped_column(Text)
    merchant_key: Mapped[str] = mapped_column(Text, nullable=False, server_default=text("''"))
    predicted_category: Mapped[str | None] = mapped_column(String(32))
    predicted_source: Mapped[str | None] = mapped_column(String(16))
    confidence: Mapped[float | None] = mapped_column(Float)
    total_amount: Mapped[int | None] = mapped_column(BigInteger)
    lines: Mapped[long_text]
    content_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    seen_count: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("1"))
    status: Mapped[str] = mapped_column(String(16), nullable=False, server_default=text("'new'"))
    first_seen_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))
    last_seen_at: Mapped[timestamptz] = mapped_column(nullable=False, server_default=text("now()"))

    __table_args__ = (
        UniqueConstraint("content_hash", name="uq_bill_unknown_scans_content_hash"),
        Index("idx_bill_unknown_scans_status_merchant", "status", "merchant_key"),
        CheckConstraint("status IN ('new', 'labeled', 'ignored')", name="chk_bill_unknown_scans_status"),
        CheckConstraint("user_id_hash ~ '^[0-9a-f]{64}$'", name="chk_bill_unknown_scans_user_hash"),
        CheckConstraint("content_hash ~ '^[0-9a-f]{64}$'", name="chk_bill_unknown_scans_content_hash"),
    )


__all__ = [
    "BILL_CATEGORY_GROUPS",
    "BillClassifyStat",
    "BillBrand",
    "BillKeyword",
    "BillRuleAudit",
    "BillUnknownScan",
    "BillUserMerchantChoice",
]
