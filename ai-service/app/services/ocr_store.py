"""Lưu trữ dành cho ocr-service: toàn bộ việc ghi/đọc DB mà ocr-service từng tự làm bằng SQL, nay nằm ở đây sau ``/api/v1/ocr-store``.

Lý do: hai dịch vụ chung một DB thì mỗi lần đổi bảng phải sửa và triển khai cả hai cùng lúc. Giờ chỉ AI Service biết tên bảng; ocr-service chỉ biết các endpoint.
Hàm ở đây ``flush``/``execute`` chứ không ``commit``: router quyết định. Hành vi giữ NGUYÊN như SQL cũ của ocr-service (``storage/*_pg.py``): bill của ocr-service là mẫu thô
``source = 'receipt'`` kèm ``package_name = 'bill_scan'`` (phân biệt với văn bản OCR của ``POST /ocr``), nhãn theo 9 nhóm cũ của bộ luật bill (dòng bí danh trong ``kb_categories``).
"""

from __future__ import annotations

import json
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill.rules_admin import RuleError
from app.schemas.ocr_store import (
    BrandIn,
    BrandVote,
    KeywordIn,
    LabeledSampleIn,
    MerchantMemoryPut,
    TrainingFinish,
    TrainingStart,
    UnknownScanIn,
)

SOURCE = "receipt"
PACKAGE = "bill_scan"
TRANSACTION_TYPE = "debit"  # bill là tiền ra
# Nguồn nhãn của ocr-service -> ``source`` của sự kiện trong ml_label_events (chỉ ghi thêm: mỗi lần đặt hoặc đổi nhãn là một sự kiện mới).
EVENT_SOURCE = {"user": "user", "gold": "gold", "imported": "import", "synthetic_ocr": "system"}

JOB_RUNNING, JOB_OK, JOB_FAILED = "running", "succeeded", "failed"
MODEL_ACTIVE, MODEL_REJECTED, MODEL_RETIRED = "active", "rejected", "retired"
EVAL_SPLIT = "cv"  # kiểm tra chéo nhóm theo cửa hàng trên bill thật (chưa có tập test riêng)


def _invalid(code: str, message: str) -> RuleError:
    return RuleError(422, code, message)


async def _require_slugs(session: AsyncSession, slugs: set[str]) -> None:
    if not slugs:
        return
    known = set((await session.execute(text("SELECT slug FROM kb_categories WHERE slug = ANY(:slugs)"), {"slugs": list(slugs)})).scalars())
    if unknown := sorted(slugs - known):
        raise _invalid("unknown_category", f"Danh mục chưa có trong kb_categories: {unknown}")


# ------------------------------------------------------------------ kiến thức (thương hiệu, từ khóa)
async def knowledge(session: AsyncSession, include_inactive: bool) -> dict[str, list[dict[str, Any]]]:
    where = "" if include_inactive else "WHERE is_active"
    brands = (await session.execute(text(f"SELECT brand_key, category_slug, display_name, whole_word, is_active, source FROM kb_brands {where} ORDER BY brand_key"))).all()
    # Đã sắp theo category_priority: người nạp giữ đúng thứ tự ưu tiên giữa các nhóm.
    keywords = (
        await session.execute(
            text(f"SELECT kind, category_slug, keyword, category_priority, is_active FROM kb_keywords {where} ORDER BY category_priority, category_slug, keyword")
        )
    ).all()
    return {
        "brands": [dict(brand_key=r[0], category_slug=r[1], display_name=r[2], whole_word=bool(r[3]), is_active=bool(r[4]), source=r[5]) for r in brands],
        "keywords": [dict(kind=r[0], category_slug=r[1], keyword=r[2], category_priority=r[3], is_active=bool(r[4])) for r in keywords],
    }


async def seed_knowledge(session: AsyncSession, brands: list[BrandIn], keywords: list[KeywordIn]) -> dict[str, int]:
    """Chỉ thêm mục còn thiếu. Thương hiệu: theo ``brand_key``. Từ khóa: theo ``(kind, keyword)`` bất kể nhóm (từ đã được chuyển nhóm thì không thêm lại ở nhóm cũ)."""
    await _require_slugs(session, {b.category_slug for b in brands} | {k.category_slug for k in keywords})
    have_keywords = {(r[0], r[1]) for r in (await session.execute(text("SELECT kind, keyword FROM kb_keywords"))).all()}
    added = {"brands": 0, "keywords": 0}
    for b in brands:
        result = await session.execute(
            text("INSERT INTO kb_brands (brand_key, category_slug, display_name, whole_word, is_active, source) VALUES (:k, :c, :d, :w, TRUE, 'seed') ON CONFLICT (brand_key) DO NOTHING"),
            {"k": b.brand_key, "c": b.category_slug, "d": b.display_name, "w": b.whole_word},
        )
        added["brands"] += max(result.rowcount or 0, 0)
    for k in keywords:
        if (k.kind, k.keyword) in have_keywords:
            continue
        result = await session.execute(
            text("INSERT INTO kb_keywords (kind, category_slug, keyword, category_priority, is_active, source) VALUES (:kind, :c, :kw, :p, TRUE, 'seed') ON CONFLICT (kind, category_slug, keyword) DO NOTHING"),
            {"kind": k.kind, "c": k.category_slug, "kw": k.keyword, "p": k.category_priority},
        )
        added["keywords"] += max(result.rowcount or 0, 0)
    return added


async def propose_brand(session: AsyncSession, brand: BrandVote) -> bool:
    """Thêm thương hiệu từ phiếu bầu (nguồn ``vote``). Không bao giờ ghi đè dòng đã có do người khác thêm. True nếu thêm mới."""
    await _require_slugs(session, {brand.category_slug})
    result = await session.execute(
        text("INSERT INTO kb_brands (brand_key, category_slug, display_name, whole_word, is_active, source) VALUES (:k, :c, :d, :w, :a, 'vote') ON CONFLICT (brand_key) DO NOTHING"),
        {"k": brand.brand_key, "c": brand.category_slug, "d": brand.display_name, "w": brand.whole_word, "a": brand.is_active},
    )
    return (result.rowcount or 0) > 0


async def activate_voted_brand(session: AsyncSession, brand_key: str) -> bool:
    """Bật thương hiệu CHỜ DUYỆT do phiếu bầu thêm (nguồn ``vote``, đang tắt). Dòng của người khác hay đã bật thì không đụng. True nếu có bật."""
    result = await session.execute(
        text("UPDATE kb_brands SET is_active = TRUE, updated_at = now() WHERE brand_key = :k AND source = 'vote' AND is_active = FALSE"), {"k": brand_key}
    )
    return (result.rowcount or 0) > 0


async def merchant_consensus(session: AsyncSession) -> list[dict[str, Any]]:
    """Phiếu của các bill người dùng ĐÃ đồng ý đóng góp (``v_merchant_consensus``): mỗi người một phiếu cho mỗi cửa hàng."""
    rows = (await session.execute(text("SELECT merchant_key, category_slug, users FROM v_merchant_consensus"))).all()
    return [dict(merchant_key=r[0], category_slug=r[1], users=r[2]) for r in rows]


# ------------------------------------------------------------------ cửa hàng người dùng đã chốt
async def merchant_memory(session: AsyncSession, user_id_hash: str) -> list[dict[str, Any]]:
    rows = (await session.execute(text("SELECT merchant_key, category_slug, display_name FROM user_merchant_memory WHERE user_id_hash = :u"), {"u": user_id_hash})).all()
    return [dict(merchant_key=r[0], category_slug=r[1], display_name=r[2]) for r in rows]


async def put_merchant_memory(session: AsyncSession, body: MerchantMemoryPut) -> None:
    try:
        await session.execute(
            text(
                "INSERT INTO user_merchant_memory (user_id_hash, merchant_key, display_name, category_slug) VALUES (:u, :k, :n, :c) "
                "ON CONFLICT (user_id_hash, merchant_key) DO UPDATE SET category_slug = EXCLUDED.category_slug, "
                "display_name = COALESCE(EXCLUDED.display_name, user_merchant_memory.display_name), updated_at = now()"
            ),
            {"u": body.user_id_hash, "k": body.merchant_key.lower(), "n": body.display_name, "c": body.category_slug},
        )
        await session.flush()
    except Exception as error:  # noqa: BLE001 - trigger loại danh mục hoặc khóa ngoại: dữ liệu sai chứ không phải lỗi máy chủ
        await session.rollback()
        raise _invalid("invalid_category", "Danh mục phải là một khoản chi của backend (không nhận thu nhập hay nhóm cũ).") from error


# ------------------------------------------------------------------ bill đã gắn nhãn
async def append_labeled(session: AsyncSession, sample: LabeledSampleIn) -> None:
    """Thêm (hoặc cập nhật theo nội dung) một bill đã gắn nhãn: mẫu thô + nhãn hiện tại + MỘT sự kiện nhãn mới. Nhãn mới nhất của cùng một bill thắng; đã là gold thì giữ gold."""
    await _require_slugs(session, {sample.category_slug})
    now = sample.ts or datetime.now(UTC)
    if now.tzinfo is None:  # ocr-service ghi thời điểm quét không kèm múi giờ (giờ máy chủ của nó): coi là UTC thay vì để driver từ chối
        now = now.replace(tzinfo=UTC)
    raw_id = (
        await session.execute(
            text(
                "INSERT INTO ml_raw_samples (source, package_name, notification_title, notification_body, received_at, user_id_hash, content_hash) "
                "VALUES (:source, :package, :title, :body, :ts, :u, :h) ON CONFLICT (content_hash) DO UPDATE SET content_hash = EXCLUDED.content_hash RETURNING id"
            ),
            {"source": SOURCE, "package": PACKAGE, "title": sample.merchant, "body": "\n".join(sample.lines), "ts": now, "u": sample.user_id_hash, "h": sample.content_hash},
        )
    ).scalar_one()
    await session.execute(
        text(
            "INSERT INTO ml_labeled_samples (raw_sample_id, is_financial, transaction_type, amount_cents, merchant_name, category_slug, labeled_by, labeled_at, is_gold) "
            "VALUES (:raw, TRUE, :t, :amount, :m, :c, :by, now(), :gold) "
            "ON CONFLICT (raw_sample_id) DO UPDATE SET merchant_name = EXCLUDED.merchant_name, category_slug = EXCLUDED.category_slug, "
            "amount_cents = EXCLUDED.amount_cents, labeled_by = EXCLUDED.labeled_by, labeled_at = EXCLUDED.labeled_at, "
            "is_gold = (ml_labeled_samples.is_gold OR EXCLUDED.is_gold)"
        ),
        {"raw": raw_id, "t": TRANSACTION_TYPE, "amount": sample.total_amount, "m": sample.merchant, "c": sample.category_slug, "by": sample.label_source, "gold": sample.is_gold},
    )
    await session.execute(
        text(
            "INSERT INTO ml_label_events (labeled_sample_id, category_slug, is_financial, source) "
            "SELECT id, category_slug, is_financial, :source FROM ml_labeled_samples WHERE raw_sample_id = :raw"
        ),
        {"source": EVENT_SOURCE[sample.label_source], "raw": raw_id},
    )


_LABELED_FROM = (
    "FROM ml_labeled_samples l JOIN ml_raw_samples r ON r.id = l.raw_sample_id "
    "WHERE r.source = :source AND r.package_name = :package AND l.category_slug IS NOT NULL"
)


async def list_labeled(session: AsyncSession, limit: int, offset: int) -> dict[str, Any]:
    """Bill đã gắn nhãn của MỌI người dùng (dùng cho huấn luyện chung), theo thời gian."""
    params = {"source": SOURCE, "package": PACKAGE}
    total = (await session.execute(text(f"SELECT count(*) {_LABELED_FROM}"), params)).scalar_one()
    rows = (
        await session.execute(
            text(f"SELECT r.received_at, r.notification_body, l.merchant_name, l.category_slug, l.labeled_by, l.amount_cents {_LABELED_FROM} ORDER BY r.received_at, r.id LIMIT :limit OFFSET :offset"),
            {**params, "limit": limit, "offset": offset},
        )
    ).all()
    items = [
        dict(ts=r[0].isoformat(), lines=r[1].split("\n") if r[1] else [], merchant=r[2], category=r[3], label_source=r[4], total_amount=r[5])
        for r in rows
    ]
    return {"items": items, "total": total}


async def count_labeled_by(session: AsyncSession, label_source: str) -> int:
    return (await session.execute(text("SELECT count(*) FROM ml_labeled_samples WHERE labeled_by = :by"), {"by": label_source})).scalar_one()


async def claim_gold(session: AsyncSession, total: int, category_slug: str, dry_run: bool) -> str:
    """Bộ chuẩn (gold): ``already`` nếu đã có bill gold khớp (tổng tiền, danh mục); ``marked`` khi ĐÚNG MỘT bill nhập sẵn khớp (đánh dấu gold, trừ khi ``dry_run``); ``missing`` còn lại."""
    rows = (
        await session.execute(
            text("SELECT id, is_gold FROM ml_labeled_samples WHERE amount_cents = :t AND category_slug = :c AND labeled_by IN ('imported', 'gold')"),
            {"t": total, "c": category_slug},
        )
    ).all()
    if any(r[1] for r in rows):
        return "already"
    if len(rows) == 1:
        if not dry_run:
            await session.execute(text("UPDATE ml_labeled_samples SET is_gold = TRUE WHERE id = :id"), {"id": rows[0][0]})
        return "marked"
    return "missing"


async def gold_merchant_names(session: AsyncSession) -> dict[str, list[str]]:
    known = (
        await session.execute(
            text(
                "SELECT DISTINCT l.merchant_name FROM ml_labeled_samples l JOIN ml_raw_samples r ON r.id = l.raw_sample_id "
                "WHERE l.labeled_by <> 'gold' AND l.merchant_name IS NOT NULL AND l.merchant_name <> '' AND r.source = :source AND r.package_name = :package"
            ),
            {"source": SOURCE, "package": PACKAGE},
        )
    ).scalars().all()
    gold = (
        await session.execute(text("SELECT DISTINCT merchant_name FROM ml_labeled_samples WHERE labeled_by = 'gold' AND merchant_name IS NOT NULL AND merchant_name <> ''"))
    ).scalars().all()
    return {"known": list(known), "gold": list(gold)}


async def rename_gold(session: AsyncSession, old: str, new: str) -> int:
    await session.execute(
        text(
            "UPDATE ml_raw_samples SET notification_title = :new WHERE source = :source AND package_name = :package AND notification_title = :old "
            "AND id IN (SELECT raw_sample_id FROM ml_labeled_samples WHERE labeled_by = 'gold')"
        ),
        {"new": new, "old": old, "source": SOURCE, "package": PACKAGE},
    )
    result = await session.execute(text("UPDATE ml_labeled_samples SET merchant_name = :new WHERE labeled_by = 'gold' AND merchant_name = :old"), {"new": new, "old": old})
    return result.rowcount or 0


# ------------------------------------------------------------------ bill lạ (hàng đợi duyệt)
async def record_unknown_scan(session: AsyncSession, scan: UnknownScanIn) -> None:
    await session.execute(
        text(
            "INSERT INTO bill_unknown_scans (user_id_hash, merchant_read, merchant_key, predicted_category, predicted_source, confidence, total_amount, lines, content_hash, seen_count, status) "
            "VALUES (:u, :read, :key, :cat, :src, :conf, :total, :lines, :h, 1, 'new') "
            "ON CONFLICT (content_hash) DO UPDATE SET seen_count = bill_unknown_scans.seen_count + 1, last_seen_at = now()"
        ),
        {"u": scan.user_id_hash, "read": scan.merchant_read, "key": scan.merchant_key, "cat": scan.predicted_category, "src": scan.predicted_source,
         "conf": scan.confidence, "total": scan.total_amount, "lines": "\n".join(scan.lines), "h": scan.content_hash},
    )


async def label_unknown_scans(session: AsyncSession, merchant_key: str) -> int:
    """Người dùng đã chốt danh mục cho cửa hàng này: các bill lạ cùng tên không cần xem xét nữa."""
    result = await session.execute(text("UPDATE bill_unknown_scans SET status = 'labeled' WHERE merchant_key = :k AND status = 'new'"), {"k": merchant_key})
    return result.rowcount or 0


# ------------------------------------------------------------------ lịch sử huấn luyện
async def _model_id(session: AsyncSession, stage: str) -> Any:
    model_id = (await session.execute(text("SELECT id FROM ml_models WHERE code = :c"), {"c": stage})).scalar_one_or_none()
    if model_id is None:
        raise _invalid("unknown_model", f"Model {stage!r} chưa được đăng ký ở ml_models (thêm một dòng ml_models rồi thử lại).")
    return model_id


async def start_training(session: AsyncSession, body: TrainingStart) -> str:
    model_id = await _model_id(session, body.stage)
    job_id = (
        await session.execute(
            text("INSERT INTO ml_training_jobs (model_id, status, params, started_at) VALUES (:m, :s, CAST(:p AS jsonb), now()) RETURNING id"),
            {"m": model_id, "s": JOB_RUNNING, "p": json.dumps(body.params, ensure_ascii=False)},
        )
    ).scalar_one()
    return str(job_id)


async def fail_training(session: AsyncSession, job_id: str, error: str) -> bool:
    result = await session.execute(
        text("UPDATE ml_training_jobs SET status = :s, error_message = :e, finished_at = now() WHERE id = CAST(:id AS uuid)"), {"s": JOB_FAILED, "e": error[:2000], "id": job_id}
    )
    return (result.rowcount or 0) > 0


async def finish_training(session: AsyncSession, job_id: str, body: TrainingFinish) -> str | None:
    """Ghi kết quả một lần huấn luyện; trả id phiên bản model, hoặc None khi không có lần chạy này."""
    model_id = await _model_id(session, body.stage)
    updated = await session.execute(
        text("UPDATE ml_training_jobs SET status = :s, sample_count = :n, finished_at = now() WHERE id = CAST(:id AS uuid)"), {"s": JOB_OK, "n": body.n_records, "id": job_id}
    )
    if not updated.rowcount:
        return None
    metrics = {"cv_accuracy": body.cv_accuracy, "macro_f1": body.macro_f1, "classes": body.classes, "reason": body.reason,
               "gate": {"min_cv_accuracy": body.min_cv_accuracy, "min_records": body.min_records}}
    if body.trained:  # bản active cũ chuyển 'retired' để chỉ có một bản đang chạy
        await session.execute(text("UPDATE ml_model_versions SET status = :retired, updated_at = now() WHERE model_id = :m AND status = :active"),
                              {"retired": MODEL_RETIRED, "m": model_id, "active": MODEL_ACTIVE})
    version_id = (
        await session.execute(
            text(
                "INSERT INTO ml_model_versions (model_id, version, artifact_path, status, metrics, training_job_id, trained_at, notes) "
                "VALUES (:m, :v, :path, :s, CAST(:metrics AS jsonb), CAST(:job AS uuid), now(), :notes) RETURNING id"
            ),
            {"m": model_id, "v": body.version, "path": body.artifact_path if body.trained else "", "s": MODEL_ACTIVE if body.trained else MODEL_REJECTED,
             "metrics": json.dumps(metrics, ensure_ascii=False), "job": job_id, "notes": body.reason},
        )
    ).scalar_one()
    if body.cv_accuracy is not None:  # có số đo thì ghi cả bảng đánh giá và đo theo từng danh mục
        run_id = (
            await session.execute(
                text(
                    "INSERT INTO ml_evaluation_runs (model_version_id, split, sample_count, accuracy, macro_f1, metrics, notes) "
                    "VALUES (:v, :split, :n, :acc, :f1, CAST(:metrics AS jsonb), :notes) RETURNING id"
                ),
                {"v": version_id, "split": EVAL_SPLIT, "n": body.n_records, "acc": body.cv_accuracy, "f1": body.macro_f1 or 0.0,
                 "metrics": json.dumps({"classes": body.classes}, ensure_ascii=False), "notes": body.reason},
            )
        ).scalar_one()
        for label, (precision, recall, f1, support) in body.per_class.items():
            await session.execute(
                text('INSERT INTO ml_evaluation_category_metrics (evaluation_run_id, label, "precision", recall, f1, support) VALUES (:r, :l, :p, :rc, :f1, :s)'),
                {"r": run_id, "l": label, "p": precision, "rc": recall, "f1": f1, "s": support},
            )
    return str(version_id)
