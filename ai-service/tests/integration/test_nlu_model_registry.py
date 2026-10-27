"""Sổ model Rasa trên Postgres thật: đúng một model active, promote cho bản cũ nghỉ, CHECK trạng thái."""

import sys
from pathlib import Path

import pytest
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from app.db.models.nlu_model_version import NluModelVersion  # noqa: E402
from nlu_model import switch_active  # noqa: E402


def version(name: str, status: str = "candidate") -> NluModelVersion:
    return NluModelVersion(version=name, artifact_path=f"models/{name}.tar.gz", status=status, dataset_hash="a" * 64, dataset_counts={"train": {"greet": 1}})


async def statuses(session) -> dict[str, str]:
    return {r.version: r.status for r in (await session.scalars(select(NluModelVersion))).all()}


async def test_switch_active_retires_the_old_model_and_activates_the_new_one(session):
    old, new = version("finmate-nlu-v11", "active"), version("finmate-nlu-v12")
    session.add_all([old, new])
    await session.commit()
    await switch_active(session, new, old)
    assert await statuses(session) == {"finmate-nlu-v11": "retired", "finmate-nlu-v12": "active"}
    assert new.activated_at is not None


async def test_first_model_can_become_active_without_an_old_one(session):
    first = version("finmate-nlu-v1")
    session.add(first)
    await session.commit()
    await switch_active(session, first, None)
    assert await statuses(session) == {"finmate-nlu-v1": "active"}


async def test_the_database_refuses_two_active_models(session):
    session.add_all([version("a", "active"), version("b", "active")])
    with pytest.raises(IntegrityError):
        await session.commit()


async def test_the_database_refuses_an_unknown_status_and_a_duplicate_version(session):
    session.add(version("a", "published"))
    with pytest.raises(IntegrityError):
        await session.commit()
    await session.rollback()
    session.add_all([version("a"), version("a")])
    with pytest.raises(IntegrityError):
        await session.commit()


async def test_evaluations_round_trip_as_json(session):
    row = version("a")
    row.eval_raw, row.eval_final = {"acc": 0.9, "by_tag": {"nlu_test": {"n": 3}}}, {"acc": 0.95, "fixed": 2, "broke": 0}
    session.add(row)
    await session.commit()
    saved = (await session.scalars(select(NluModelVersion))).one()
    assert saved.eval_raw["by_tag"]["nlu_test"]["n"] == 3 and saved.eval_final["fixed"] == 2
