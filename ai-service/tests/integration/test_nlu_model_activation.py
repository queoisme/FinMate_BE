"""Trang admin liệt kê và kích hoạt phiên bản Rasa (``app/services/nlu_models.py``, ``/admin/rasa/models``): Postgres thật, Rasa giả bằng MockTransport.

Cái cần giữ: sổ, tệp ``active.tar.gz`` và model Rasa đang nạp không bao giờ lệch nhau, dù bước nào lỗi; cổng đo chặn bản tệ hơn trừ khi ép kèm lý do."""

import httpx
import pytest
from sqlalchemy import select

from app.bill.rules_admin import RuleError
from app.core.config import settings
from app.db.models.nlu_model_version import NluModelVersion
from app.services import nlu_models

ACTOR = "a" * 64


def measured(raw, final, *, test_hash="h", fixed=1, broke=0):
    return {
        "eval_raw": {"acc": raw, "test_hash": test_hash, "n": 10, "by_tag": {}},
        "eval_final": {"acc": final, "test_hash": test_hash, "n": 10, "fixed": fixed, "broke": broke, "by_tag": {}},
    }


def row(name, status="candidate", raw=0.9, final=0.95):
    return NluModelVersion(version=name, artifact_path=f"models/{name}.tar.gz", status=status, dataset_hash="a" * 64, dataset_counts={"train": {"greet": 2, "add_expense": 3}},
                           **measured(raw, final))


@pytest.fixture(autouse=True)
def clean_job():
    nlu_models._job.clear()
    yield
    nlu_models._job.clear()


@pytest.fixture
def fake_rasa(monkeypatch, tmp_path):
    """Rasa giả: ghi lại các lời gọi; ``state['put']`` là mã trả về của ``PUT /model``."""
    state = {"put": 204, "calls": [], "running": "active.tar.gz"}

    def handler(request: httpx.Request) -> httpx.Response:
        state["calls"].append((request.method, request.url.path, request.content.decode() if request.content else ""))
        if request.url.path == "/model":
            return httpx.Response(state["put"], json={"message": "không nạp được"}) if state["put"] >= 300 else httpx.Response(204)
        return httpx.Response(200, json={"model_file": state["running"]})

    real = httpx.AsyncClient
    monkeypatch.setattr(nlu_models.httpx, "AsyncClient", lambda **kwargs: real(transport=httpx.MockTransport(handler), **kwargs))
    monkeypatch.setattr(settings, "rasa_url", "http://rasa:5005")
    monkeypatch.setattr(settings, "rasa_models_dir", str(tmp_path))
    for name in ("finmate-nlu-v11", "finmate-nlu-v16-s2", "finmate-nlu-v13"):
        (tmp_path / f"{name}.tar.gz").write_bytes(name.encode())
    (tmp_path / "active.tar.gz").write_bytes(b"finmate-nlu-v11")
    state["dir"] = tmp_path
    return state


async def seed(session, *rows):
    session.add_all(rows)
    await session.commit()


async def statuses(session):
    session.expire_all()
    return {r.version: r.status for r in (await session.scalars(select(NluModelVersion))).all()}


async def activate(session, version, **kw):
    """Gọi activate rồi chờ tác vụ nạp nền xong (dòng chạy thật trả 202 ngay và không chờ)."""
    result = await nlu_models.activate(session, version, actor=ACTOR, expected_active=kw.pop("expected_active", None), force=kw.pop("force", False), reason=kw.pop("reason", None))
    assert result["status"] == "loading"
    await nlu_models.wait_for_job()
    return result


async def test_a_better_candidate_becomes_active_and_the_file_restart_would_load(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v16-s2", raw=0.95, final=0.97))
    result = await activate(session, "finmate-nlu-v16-s2", expected_active="finmate-nlu-v11")
    assert result["previous_version"] == "finmate-nlu-v11" and result["forced"] is False
    assert await statuses(session) == {"finmate-nlu-v11": "retired", "finmate-nlu-v16-s2": "active"}
    assert (fake_rasa["dir"] / "active.tar.gz").read_bytes() == b"finmate-nlu-v16-s2"  # Rasa khởi động lại sẽ nạp đúng bản này
    assert not (fake_rasa["dir"] / "active.tar.gz.tmp").exists()
    assert ("PUT", "/model", '{"model_file":"models/finmate-nlu-v16-s2.tar.gz"}') in fake_rasa["calls"]


async def test_a_worse_candidate_is_blocked_and_nothing_changes(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active", raw=0.9, final=0.98), row("finmate-nlu-v13", raw=0.95, final=0.96))
    with pytest.raises(RuleError) as caught:
        await activate(session, "finmate-nlu-v13")
    assert (caught.value.status, caught.value.code) == (422, "blocked_by_gate") and any("tụt" in reason for reason in caught.value.details)
    assert await statuses(session) == {"finmate-nlu-v11": "active", "finmate-nlu-v13": "candidate"}
    assert (fake_rasa["dir"] / "active.tar.gz").read_bytes() == b"finmate-nlu-v11"
    assert not any(call[1] == "/model" for call in fake_rasa["calls"])  # chưa hề bảo Rasa đổi


async def test_forcing_a_blocked_version_needs_a_reason_and_leaves_a_trail_in_the_ledger(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active", raw=0.9, final=0.98), row("finmate-nlu-v13", raw=0.95, final=0.96))
    with pytest.raises(RuleError) as caught:
        await activate(session, "finmate-nlu-v13", force=True)
    assert caught.value.code == "reason_required"
    result = await activate(session, "finmate-nlu-v13", force=True, reason="thử bản này trên người dùng nội bộ")
    assert result["forced"] is True and result["gate_reasons"]
    session.expire_all()  # tác vụ nền ghi sổ bằng phiên riêng
    saved = await session.scalar(select(NluModelVersion).where(NluModelVersion.version == "finmate-nlu-v13"))
    assert saved.status == "active" and "ÉP qua cổng đo" in saved.notes and "người dùng nội bộ" in saved.notes


async def test_rasa_refusing_to_load_leaves_the_ledger_and_the_file_untouched(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v16-s2", raw=0.95, final=0.97))
    fake_rasa["put"] = 400
    await activate(session, "finmate-nlu-v16-s2")
    job = nlu_models.current_job()
    assert job["state"] == "failed" and "Model đang chạy giữ nguyên" in job["error"]  # lỗi hiện ở tiến trình, không ở yêu cầu (đã trả 202)
    assert await statuses(session) == {"finmate-nlu-v11": "active", "finmate-nlu-v16-s2": "candidate"}
    assert (fake_rasa["dir"] / "active.tar.gz").read_bytes() == b"finmate-nlu-v11"
    assert not (fake_rasa["dir"] / "active.tar.gz.tmp").exists()


async def test_going_back_to_a_retired_version_works(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "retired", raw=0.9, final=0.98), row("finmate-nlu-v16-s2", "active", raw=0.95, final=0.99))
    result = await activate(session, "finmate-nlu-v11", force=True, reason="quay lại bản cũ vì lỗi ngoài đời")
    assert result["previous_version"] == "finmate-nlu-v16-s2"
    assert await statuses(session) == {"finmate-nlu-v11": "active", "finmate-nlu-v16-s2": "retired"}


@pytest.mark.parametrize(
    ("version", "kwargs", "status", "code"),
    [
        ("finmate-nlu-v11", {}, 409, "already_active"),
        ("khong-co", {}, 404, "model_not_found"),
        ("finmate-nlu-v16-s2", {"expected_active": "ban-khac"}, 409, "stale_active"),
    ],
)
async def test_bad_requests_are_refused_with_a_clear_code(session, fake_rasa, version, kwargs, status, code):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v16-s2", raw=0.95, final=0.97))
    with pytest.raises(RuleError) as caught:
        await activate(session, version, **kwargs)
    assert (caught.value.status, caught.value.code) == (status, code)


async def test_a_version_whose_file_is_gone_is_refused_before_anything_changes(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v99", raw=0.95, final=0.97))
    with pytest.raises(RuleError) as caught:
        await activate(session, "finmate-nlu-v99")
    assert caught.value.code == "artifact_missing" and await statuses(session) == {"finmate-nlu-v11": "active", "finmate-nlu-v99": "candidate"}


async def test_without_the_models_folder_listing_works_but_activating_is_refused(session, monkeypatch):
    monkeypatch.setattr(settings, "rasa_models_dir", None)
    await seed(session, row("finmate-nlu-v11", "active"))
    assert (await nlu_models.overview(session))["can_activate"] is False
    with pytest.raises(RuleError) as caught:
        await activate(session, "finmate-nlu-v11")
    assert caught.value.code == "rasa_not_configured"


async def test_the_list_shows_every_version_with_its_gate_verdict_and_whether_rasa_is_in_sync(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active", raw=0.9, final=0.98), row("finmate-nlu-v13", raw=0.95, final=0.96), row("finmate-nlu-v16-s2", raw=0.95, final=0.99))
    data = await nlu_models.overview(session)
    by_name = {item["version"]: item for item in data["items"]}
    assert data["active_version"] == "finmate-nlu-v11" and data["in_sync"] is True and data["can_activate"] is True
    assert by_name["finmate-nlu-v11"]["gate"] is None
    assert by_name["finmate-nlu-v13"]["gate"]["allowed"] is False and by_name["finmate-nlu-v16-s2"]["gate"] == {"allowed": True, "reasons": []}
    assert by_name["finmate-nlu-v13"]["train_total"] == 5 and by_name["finmate-nlu-v13"]["eval"]["final_acc"] == 0.96
    fake_rasa["running"] = "mot-model-la.tar.gz"
    assert (await nlu_models.overview(session))["in_sync"] is False  # Rasa đang chạy tệp khác với bản sổ ghi là đang chạy


async def test_http_routes_list_and_activate_with_the_actor_header(client, session, fake_rasa, auth_headers):
    await seed(session, row("finmate-nlu-v11", "active", raw=0.9, final=0.98), row("finmate-nlu-v13", raw=0.95, final=0.96))
    assert (await client.get("/api/v1/admin/rasa/models")).status_code in (401, 403)  # cần khóa nội bộ
    listed = await client.get("/api/v1/admin/rasa/models", headers=auth_headers)
    assert listed.status_code == 200 and listed.json()["active_version"] == "finmate-nlu-v11" and len(listed.json()["items"]) == 2
    actor = {**auth_headers, "X-Actor-Hash": ACTOR}
    blocked = await client.post("/api/v1/admin/rasa/models/finmate-nlu-v13/activate", json={}, headers=actor)
    assert blocked.status_code == 422 and blocked.json()["error"]["code"] == "blocked_by_gate"
    assert (await client.post("/api/v1/admin/rasa/models/finmate-nlu-v13/activate", json={}, headers=auth_headers)).status_code == 422  # thiếu người thực hiện
    forced = await client.post("/api/v1/admin/rasa/models/finmate-nlu-v13/activate", json={"force": True, "reason": "thử nghiệm nội bộ"}, headers=actor)
    assert forced.status_code == 202 and forced.json()["forced"] is True and forced.json()["status"] == "loading"  # nạp mất vài phút nên trả ngay, nạp ở nền
    await nlu_models.wait_for_job()
    assert (await client.get("/api/v1/admin/rasa/models", headers=auth_headers)).json()["activation"]["state"] == "done"


async def test_a_second_activation_while_one_is_loading_is_refused(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v16-s2", raw=0.95, final=0.97), row("finmate-nlu-v13", raw=0.95, final=0.97))
    first = await nlu_models.activate(session, "finmate-nlu-v16-s2", actor=ACTOR, expected_active=None, force=False, reason=None)  # chưa chờ: tác vụ nền đang chạy
    assert first["status"] == "loading" and nlu_models.current_job()["state"] == "loading"
    with pytest.raises(RuleError) as caught:
        await nlu_models.activate(session, "finmate-nlu-v13", actor=ACTOR, expected_active=None, force=False, reason=None)
    assert (caught.value.status, caught.value.code) == (409, "activation_in_progress")
    await nlu_models.wait_for_job()
    assert nlu_models.current_job()["state"] == "done"


async def test_when_rasa_runs_another_file_than_the_ledger_activating_the_active_version_resyncs_without_touching_the_ledger(session, fake_rasa):
    await seed(session, row("finmate-nlu-v11", "active"), row("finmate-nlu-v16-s2", raw=0.95, final=0.97))
    with pytest.raises(RuleError) as caught:  # đang khớp: không có gì để làm
        await activate(session, "finmate-nlu-v11")
    assert caught.value.code == "already_active"
    fake_rasa["running"] = "finmate-nlu-v16-s2.tar.gz"  # ví dụ lần nạp trước bị ngắt giữa chừng: Rasa chạy bản khác sổ
    result = await activate(session, "finmate-nlu-v11")
    assert result["resync"] is True and nlu_models.current_job()["state"] == "done"
    assert await statuses(session) == {"finmate-nlu-v11": "active", "finmate-nlu-v16-s2": "candidate"}
    assert ("PUT", "/model", '{"model_file":"models/active.tar.gz"}') in fake_rasa["calls"]  # nạp lại đúng tệp mà khởi động lại cũng sẽ nạp
