"""``ml_models`` và bộ ``ml_*`` dùng chung (migration 0023): model thông báo và model hóa đơn nằm cùng bảng mà không lẫn nhau."""

import uuid
from datetime import UTC, datetime

import pytest
from sqlalchemy import select, text
from sqlalchemy.exc import IntegrityError

from app.core.enums import JobStatus, ModelStage, ModelStatus, SplitName
from app.db.models.model_version import ModelVersion, TrainingJob
from app.db.repositories import model_repo, sample_repo


async def add_version(session, code: str, version: str, status: str) -> ModelVersion:
    model_id = await model_repo.model_id_for(session, code)
    row = ModelVersion(model_id=model_id, version=version, artifact_path=f"{code}/{version}.joblib", status=status)
    session.add(row)
    await session.commit()
    return row


async def rejected(session, sql: str, **params) -> bool:
    try:
        await session.execute(text(sql), params)
        await session.commit()
        return False
    except IntegrityError:
        await session.rollback()
        return True


async def test_models_are_registered_and_unknown_codes_are_refused(session):
    codes = set((await session.execute(text("SELECT code FROM ml_models"))).scalars())
    assert {"classifier", "categorizer", "bill_slug", "bill_category"} <= codes
    assert await model_repo.model_id_for(session, "bill_slug")
    with pytest.raises(ValueError):
        await model_repo.model_id_for(session, "no_such_model")


async def test_stage_reads_the_model_code_and_works_in_where_clauses(session):
    await add_version(session, "classifier", "1.0.0", ModelStatus.CANDIDATE.value)
    await add_version(session, "bill_slug", "v1", ModelStatus.CANDIDATE.value)
    session.expire_all()

    rows = (await session.execute(select(ModelVersion).where(ModelVersion.stage == "bill_slug"))).scalars().all()
    assert [(r.stage, r.version) for r in rows] == [("bill_slug", "v1")]


async def test_pipeline_registry_ignores_bill_models(session):
    await add_version(session, "classifier", "1.0.0", ModelStatus.ACTIVE.value)
    await add_version(session, "bill_slug", "v1", ModelStatus.ACTIVE.value)
    await add_version(session, "bill_category", "20261001", ModelStatus.ACTIVE.value)
    session.expire_all()

    active = await model_repo.list_active(session)

    assert [(r.stage, r.version) for r in active] == [("classifier", "1.0.0")]  # vòng nạp model không gặp mã lạ như bill_slug
    assert {s.value for s in ModelStage} == {"classifier", "categorizer"}


async def test_each_model_has_at_most_one_active_version_but_models_are_independent(session):
    await add_version(session, "classifier", "1.0.0", ModelStatus.ACTIVE.value)
    await add_version(session, "bill_slug", "v1", ModelStatus.ACTIVE.value)  # model khác: được
    session.add(ModelVersion(model_id=await model_repo.model_id_for(session, "classifier"), version="1.0.1", artifact_path="x", status="active"))
    with pytest.raises(IntegrityError):
        await session.commit()
    await session.rollback()


async def test_promote_retires_the_previous_active_version(session):
    old = await add_version(session, "categorizer", "1.0.0", ModelStatus.ACTIVE.value)
    new = await add_version(session, "categorizer", "1.0.1", ModelStatus.CANDIDATE.value)

    await model_repo.promote(session, new.id)
    await session.commit()
    session.expire_all()

    statuses = {r.version: r.status for r in (await session.execute(select(ModelVersion).where(ModelVersion.stage == "categorizer"))).scalars()}
    assert statuses == {"1.0.0": "retired", "1.0.1": "active"} and old.id != new.id


async def test_status_vocabulary_is_shared(session):
    insert = "INSERT INTO ml_model_versions (model_id, version, artifact_path, status) SELECT id, :v, 'x', :s FROM ml_models WHERE code = 'classifier'"
    for i, status in enumerate(("candidate", "active", "retired", "rejected")):
        assert not await rejected(session, insert, v=f"v{i}", s=status)
    assert await rejected(session, insert, v="vx", s="archived")  # tên cũ đã đổi thành retired


async def test_training_job_needs_a_registered_model_and_a_sane_timeline(session):
    model_id = await model_repo.model_id_for(session, "bill_slug")
    started = datetime.now(UTC)
    session.add(TrainingJob(model_id=model_id, status=JobStatus.SUCCEEDED.value, started_at=started, finished_at=started, sample_count=10))
    await session.commit()
    job = (await session.execute(select(TrainingJob))).scalars().one()
    assert job.stage == "bill_slug"

    insert = "INSERT INTO ml_training_jobs (model_id, status, started_at, finished_at) VALUES (:m, 'succeeded', now(), now() - interval '1 hour')"
    assert await rejected(session, insert, m=model_id)  # kết thúc trước khi bắt đầu
    assert await rejected(session, "INSERT INTO ml_training_jobs (model_id, status, started_at) VALUES (:m, 'finished', now())", m=model_id)  # 'finished' cũ của bill


async def test_receipt_labels_and_notification_labels_share_one_table_but_not_the_training_split(session):
    raw = "INSERT INTO ml_raw_samples (id, source, package_name, notification_body, received_at, content_hash) VALUES (:id, :s, 'p', 'x', now(), :h)"
    label = "INSERT INTO ml_labeled_samples (raw_sample_id, is_financial, transaction_type, category_slug, labeled_by) VALUES (:id, true, :t, :c, :by)"
    bill, bank = uuid.uuid4(), uuid.uuid4()
    await session.execute(text(raw), {"id": bill, "s": "receipt", "h": "b" * 64})
    await session.execute(text(raw), {"id": bank, "s": "notification", "h": "c" * 64})
    await session.execute(text(label), {"id": bill, "t": "debit", "c": "dining", "by": "gold"})  # nhóm cũ của bill, người gắn nhãn của bill
    await session.execute(text(label), {"id": bank, "t": "debit", "c": "food", "by": "seed"})
    await session.commit()

    assert await sample_repo.load_split(session, SplitName.TRAIN) == []  # mẫu hóa đơn không có dòng ml_sample_splits nên không lọt vào train/evaluate của thông báo
    assert await rejected(session, label, id=bill, t="expense", c="dining", by="user")  # 'expense' phải là 'debit'


async def test_evaluation_accepts_cross_validation_runs_and_rejects_nonsense(session):
    version_id = (await add_version(session, "bill_category", "v1", ModelStatus.ACTIVE.value)).id  # lấy id trước: rollback làm đối tượng hết hạn
    insert = "INSERT INTO ml_evaluation_runs (model_version_id, split, sample_count, accuracy, macro_f1) VALUES (:v, :s, :n, :a, 0.5)"
    assert not await rejected(session, insert, v=version_id, s="cv", n=30, a=0.9)
    assert await rejected(session, insert, v=version_id, s="bogus", n=30, a=0.9)
    assert await rejected(session, insert, v=version_id, s="test", n=30, a=1.5)
    assert await rejected(session, insert, v=version_id, s="test", n=-1, a=0.9)
