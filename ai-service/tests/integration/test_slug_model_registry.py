"""Vòng đời phiên bản mô hình nhóm hóa đơn trên Postgres thật: train chỉ tạo candidate, promote đổi active và cho bản trước nghỉ."""

import pytest
from sqlalchemy import select

from app.bill.slug_model import STAGE, TrainResult, _pipeline, load_active, promote, record_training, retire
from app.db.models.model_version import ModelVersion, TrainingJob


def candidate() -> TrainResult:
    pipeline = _pipeline().fit(["pho bun com", "xang taxi gui xe", "pho com bun", "taxi xang gui xe"], ["food", "transport", "food", "transport"])
    return TrainResult("candidate", "Đạt", 0.9, 0.5, pipeline, {"samples": 4})


def rejected() -> TrainResult:
    return TrainResult("rejected", "Chưa đủ dữ liệu: cần thêm 296 mẫu có nhãn", None, None, None, {"samples": 4})


async def statuses(session) -> dict[str, str]:
    rows = (await session.execute(select(ModelVersion).where(ModelVersion.stage == STAGE))).scalars().all()
    return {r.version: r.status for r in rows}


async def test_train_never_activates(session, tmp_path):
    version = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    assert (await statuses(session)) == {version: "candidate"}
    assert (tmp_path / STAGE / f"{version}.joblib").exists()
    assert (await session.execute(select(TrainingJob).where(TrainingJob.stage == STAGE))).scalars().one().sample_count == 4


async def test_rejected_is_recorded_with_reason_and_no_artifact(session, tmp_path):
    version = await record_training(session, rejected(), sample_count=4, registry_path=str(tmp_path))
    row = (await session.execute(select(ModelVersion))).scalars().one()
    assert row.version == version and row.status == "rejected" and row.artifact_path == ""
    assert "296" in row.notes
    assert not list(tmp_path.rglob("*.joblib"))


async def test_promote_activates_candidate_and_retires_previous(session, tmp_path):
    first = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    await promote(session, first)
    second = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    assert (await statuses(session))[first] == "active"
    await promote(session, second)
    assert await statuses(session) == {first: "retired", second: "active"}


async def test_promote_refuses_a_rejected_or_unknown_version(session, tmp_path):
    version = await record_training(session, rejected(), sample_count=4, registry_path=str(tmp_path))
    with pytest.raises(ValueError):
        await promote(session, version)
    with pytest.raises(ValueError):
        await promote(session, "does-not-exist")
    assert "active" not in (await statuses(session)).values()


async def test_retire_deactivates_the_active_version(session, tmp_path):
    version = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    await promote(session, version)
    assert await retire(session) == 1
    assert await statuses(session) == {version: "retired"}
    assert await retire(session) == 0


async def test_load_active_returns_the_promoted_model_and_none_before_or_after(session, tmp_path):
    assert await load_active(session) is None
    version = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    assert await load_active(session) is None  # candidate chưa được phục vụ
    await promote(session, version)
    model = await load_active(session)
    assert model is not None and model.predict(None, ["pho bun"])[0] == "food"
    await retire(session)
    assert await load_active(session) is None


async def test_load_active_ignores_a_missing_or_corrupt_artifact(session, tmp_path):
    version = await record_training(session, candidate(), sample_count=4, registry_path=str(tmp_path))
    await promote(session, version)
    (tmp_path / STAGE / f"{version}.joblib").write_bytes(b"garbage")
    assert await load_active(session) is None
    (tmp_path / STAGE / f"{version}.joblib").unlink()
    assert await load_active(session) is None
