"""Phiên bản model Rasa NLU cho trang admin: liệt kê sổ ``nlu_model_versions`` và kích hoạt một phiên bản (kể cả quay về bản cũ).

Cổng đo ``decide_promotion`` là MỘT chỗ duy nhất, ``scripts/nlu_model.py promote`` (dòng lệnh) và API này cùng dùng. Khác dòng lệnh: API không khởi động lại Rasa mà bảo Rasa
nạp nóng (``PUT /model``, đã thử trên Rasa 3.6.21: 204 và đổi ngay; đường dẫn hỏng trả 400 và giữ model cũ).

NẠP MODEL MẤT VÀI PHÚT (đo được 2,5 phút trên máy dev) và trong lúc nạp Rasa không trả lời (chat chạy bằng luật, không ngừng dịch vụ). Nên kích hoạt chạy NỀN: ``activate`` kiểm tra,
chép bản chọn ra tệp tạm rồi trả ngay ``status: loading``; tác vụ nền mới bảo Rasa nạp, và chỉ khi Rasa nạp xong mới ghi sổ rồi đổi ``active.tar.gz`` (Rasa khởi động lại sẽ đọc tệp này).
Lỗi ở bước nào cũng không để sổ và tệp lệch nhau; tiến trình và lỗi nằm ở ``activation`` trong ``overview``. Nếu Rasa vẫn lệch sổ (ví dụ tiến trình bị ngắt giữa chừng),
``in_sync`` là False và kích hoạt lại chính bản đang chạy sẽ nạp lại ``active.tar.gz`` cho khớp.
"""
from __future__ import annotations

import asyncio
import os
import shutil
from datetime import datetime, timezone
from pathlib import Path

import httpx
import structlog
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from app.bill.rules_admin import RuleError
from app.core.config import settings
from app.core.enums import NluModelStatus
from app.db.models.nlu_model_version import NluModelVersion

logger = structlog.get_logger(__name__)

ACTIVE_FILE = "active.tar.gz"  # docker-compose.yml chạy Rasa với `-m models/active.tar.gz`
INDEPENDENT_TAGS = ("nlu_test_fresh", "nlu_test_fresh2")  # bộ chưa từng dùng để viết luật (xem audit_overrides.py): model mới không được tụt ở đây
RASA_LOAD_TIMEOUT_SECONDS = 900.0  # trần cho một lần nạp (đo được ~2,5 phút); quá thì coi là hỏng
MIN_REASON = 3

_job: dict = {}  # tác vụ nạp duy nhất đang/vừa chạy: {"version", "state": loading|done|failed, "error", "started_at", "finished_at", "task"}


def _invalid(status: int, code: str, message: str, details: list[str] | None = None) -> RuleError:
    return RuleError(status, code, message, details)


def decide_promotion(candidate: dict, active: dict | None) -> list[str]:
    """Lý do CHẶN kích hoạt (rỗng = được). candidate/active: dict có ``eval_raw`` và ``eval_final``."""
    cand_raw, cand_final = candidate.get("eval_raw"), candidate.get("eval_final")
    if not cand_raw or not cand_final:
        return ["model chưa có số đo: chạy `eval` trước"]
    reasons = []
    if cand_final["broke"] > cand_final["fixed"]:
        reasons.append(f"luật phá ({cand_final['broke']}) nhiều hơn cứu ({cand_final['fixed']}) trên model này")
    if active is None:
        return reasons
    act_raw, act_final = active.get("eval_raw"), active.get("eval_final")
    if not act_raw or not act_final:
        return [*reasons, "bản đang chạy chưa có số đo nên không so được: chạy `eval` cho nó trước"]
    if act_raw["test_hash"] != cand_raw["test_hash"]:
        return [*reasons, "tập test đã đổi từ lúc đo bản đang chạy: chạy lại `eval` cho nó để hai số đo cùng tập"]
    if cand_final["acc"] < act_final["acc"]:
        reasons.append(f"độ đúng sau luật tụt: {act_final['acc']:.2%} -> {cand_final['acc']:.2%}")
    if cand_raw["acc"] < act_raw["acc"]:
        reasons.append(f"độ đúng Rasa thô tụt: {act_raw['acc']:.2%} -> {cand_raw['acc']:.2%}")
    for tag in INDEPENDENT_TAGS:
        before, after = act_final["by_tag"].get(tag), cand_final["by_tag"].get(tag)
        if before and after and after["acc"] < before["acc"]:
            reasons.append(f"bộ độc lập {tag} tụt: {before['acc']:.2%} -> {after['acc']:.2%}")
    return reasons


def _eval_summary(row: NluModelVersion) -> dict | None:
    raw, final = row.eval_raw, row.eval_final
    if not raw or not final:
        return None
    return {
        "test_hash": raw.get("test_hash"), "n": raw.get("n"), "raw_acc": raw.get("acc"), "final_acc": final.get("acc"),
        "fixed": final.get("fixed"), "broke": final.get("broke"), "wrong_confident": raw.get("wrong_confident"),
        "by_tag": {tag: cell.get("acc") for tag, cell in (final.get("by_tag") or {}).items()},
    }


def _item(row: NluModelVersion, active: NluModelVersion | None) -> dict:
    gate = None
    if row.status != NluModelStatus.ACTIVE.value:
        reasons = decide_promotion({"eval_raw": row.eval_raw, "eval_final": row.eval_final}, {"eval_raw": active.eval_raw, "eval_final": active.eval_final} if active else None)
        gate = {"allowed": not reasons, "reasons": reasons}
    counts = (row.dataset_counts or {}).get("train") or {}
    return {
        "version": row.version, "status": row.status, "artifact_path": row.artifact_path, "created_at": row.created_at, "activated_at": row.activated_at,
        "dataset_hash": row.dataset_hash, "train_total": sum(counts.values()) if counts else None, "notes": row.notes, "eval": _eval_summary(row), "gate": gate,
    }


def current_job() -> dict | None:
    """Tiến trình kích hoạt đang/vừa chạy (không kèm đối tượng tác vụ); None khi chưa có lần nào từ lúc ai-service khởi động."""
    return {k: v for k, v in _job.items() if k != "task"} or None


async def wait_for_job() -> None:
    """Chờ tác vụ nạp nền xong (cho test; dòng chạy thật không cần chờ)."""
    task = _job.get("task")
    if task is not None:
        await task


async def _running_model_file() -> str | None:
    """Tệp Rasa đang nạp (None khi Rasa tắt, chưa cấu hình, hoặc đang bận nạp một model)."""
    if not settings.rasa_url:
        return None
    try:
        async with httpx.AsyncClient(timeout=3.0) as client:
            return (await client.get(f"{settings.rasa_url.rstrip('/')}/status")).json().get("model_file")
    except (httpx.HTTPError, ValueError):
        return None


def _in_sync(active: NluModelVersion | None, running: str | None) -> bool | None:
    return None if running is None or active is None else running in (Path(active.artifact_path).name, ACTIVE_FILE)


async def _active_row(session: AsyncSession) -> NluModelVersion | None:
    return await session.scalar(select(NluModelVersion).where(NluModelVersion.status == NluModelStatus.ACTIVE.value))


async def overview(session: AsyncSession) -> dict:
    """Mọi phiên bản trong sổ (mới nhất trước), bản đang chạy theo sổ và theo Rasa, tiến trình kích hoạt, và mỗi bản (trừ bản đang chạy) có qua cổng đo để thay bản đang chạy không."""
    rows = (await session.scalars(select(NluModelVersion).order_by(NluModelVersion.created_at.desc()))).all()
    active = next((r for r in rows if r.status == NluModelStatus.ACTIVE.value), None)
    running = await _running_model_file()
    return {
        "active_version": active.version if active else None, "running_model_file": running, "in_sync": _in_sync(active, running),
        "activation": current_job(), "can_activate": bool(settings.rasa_url and settings.rasa_models_dir), "items": [_item(r, active) for r in rows],
    }


def _models_dir() -> Path:
    if not settings.rasa_url or not settings.rasa_models_dir:
        raise _invalid(503, "rasa_not_configured", "Chưa cấu hình RASA_URL hoặc RASA_MODELS_DIR nên không kích hoạt được phiên bản từ trang admin.")
    directory = Path(settings.rasa_models_dir)
    if not directory.is_dir():
        raise _invalid(503, "rasa_models_dir_missing", f"Thư mục model của Rasa không có ({directory}): kiểm tra volume của ai-service.")
    return directory


async def _rasa_load(model_file: str) -> None:
    """Bảo Rasa nạp tệp (đường dẫn như Rasa thấy, vd. ``models/x.tar.gz``). Lỗi thì Rasa GIỮ model cũ. Chờ tới khi nạp xong (vài phút)."""
    try:
        async with httpx.AsyncClient(timeout=RASA_LOAD_TIMEOUT_SECONDS) as client:
            response = await client.put(f"{settings.rasa_url.rstrip('/')}/model", json={"model_file": model_file})
    except httpx.HTTPError as error:
        raise _invalid(502, "rasa_unavailable", f"Không gọi được Rasa ({type(error).__name__}); kiểm tra in_sync rồi kích hoạt lại bản đang chạy để đồng bộ.") from error
    if response.status_code >= 300:
        message = ""
        try:
            message = str(response.json().get("message", ""))
        except ValueError:
            pass
        raise _invalid(502, "rasa_load_failed", f"Rasa không nạp được {model_file}: {message or response.status_code}. Model đang chạy giữ nguyên.")


async def _run(factory: async_sessionmaker, version: str, artifact_path: str, directory: Path, staged: Path | None, reasons: list[str], reason: str | None, actor: str) -> None:
    """Tác vụ nền: Rasa nạp -> ghi sổ -> đổi active.tar.gz. ``staged`` là None khi chỉ đồng bộ lại Rasa với sổ."""
    try:
        if staged is None:  # đồng bộ: sổ đã đúng, Rasa đang chạy tệp khác -> nạp lại tệp mà khởi động lại cũng sẽ nạp
            await _rasa_load("models/" + ACTIVE_FILE)
        else:
            await _rasa_load(artifact_path)
            try:
                async with factory() as session:
                    row = await session.scalar(select(NluModelVersion).where(NluModelVersion.version == version))
                    active = await _active_row(session)
                    if active:
                        active.status = NluModelStatus.RETIRED.value
                        await session.flush()  # hạ bản cũ trước: index duy nhất chỉ cho một dòng active
                    row.status, row.activated_at = NluModelStatus.ACTIVE.value, datetime.now(timezone.utc)
                    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M")
                    row.notes = ((row.notes + "\n") if row.notes else "") + f"[{stamp}] kích hoạt từ trang admin (actor {actor[:12]})" + (
                        f"; ÉP qua cổng đo: {(reason or '').strip()} | {'; '.join(reasons)}" if reasons else "")
                    await session.commit()
            except Exception:
                try:
                    await _rasa_load("models/" + ACTIVE_FILE)  # sổ không ghi được: đưa Rasa về bản mà tệp active.tar.gz (chưa đổi) và sổ đang nói
                except RuleError:
                    logger.error("nlu_activation_revert_failed", version=version)
                raise
            os.replace(staged, directory / ACTIVE_FILE)  # khởi động lại Rasa sẽ nạp đúng bản này
        _job.update(state="done", error=None, finished_at=datetime.now(timezone.utc))
        logger.info("nlu_activation_done", version=version)
    except RuleError as error:
        _job.update(state="failed", error=error.message, finished_at=datetime.now(timezone.utc))
        logger.warning("nlu_activation_failed", version=version, code=error.code)
    except Exception as error:  # noqa: BLE001 - tác vụ nền không được chết im lặng
        _job.update(state="failed", error=f"Lỗi không rõ ({type(error).__name__}); xem nhật ký ai-service.", finished_at=datetime.now(timezone.utc))
        logger.exception("nlu_activation_error", version=version)
    finally:
        if staged is not None and staged.exists():
            staged.unlink()


async def activate(session: AsyncSession, version: str, *, actor: str, expected_active: str | None, force: bool, reason: str | None) -> dict:
    """Bắt đầu đưa ``version`` thành model đang chạy; trả ngay ``status: loading`` (nạp chạy nền, xem ``activation`` ở ``overview``).

    Qua cổng đo như ``promote``; ``force`` bỏ qua cổng nhưng bắt buộc có lý do (ghi vào ``notes`` của sổ). Kích hoạt lại chính bản đang chạy khi Rasa lệch sổ (``in_sync`` False)
    là ĐỒNG BỘ LẠI (``resync: true``), không đổi sổ."""
    directory = _models_dir()
    if _job.get("state") == "loading":
        raise _invalid(409, "activation_in_progress", f"Đang nạp {_job.get('version')}; chờ xong rồi thử lại.")
    row = await session.scalar(select(NluModelVersion).where(NluModelVersion.version == version))
    if row is None:
        raise _invalid(404, "model_not_found", f"Không có phiên bản {version} trong sổ.")
    active = await _active_row(session)
    previous = active.version if active else None
    if expected_active is not None and expected_active != previous:
        raise _invalid(409, "stale_active", f"Bản đang chạy đã đổi thành {previous or 'không có'} (bạn thấy {expected_active}); tải lại danh sách rồi chọn lại.")
    factory = async_sessionmaker(session.bind, expire_on_commit=False)
    started = datetime.now(timezone.utc)

    def start(staged: Path | None, reasons: list[str]) -> None:
        _job.clear()
        _job.update(version=version, state="loading", error=None, started_at=started, finished_at=None)
        _job["task"] = asyncio.create_task(_run(factory, version, row.artifact_path, directory, staged, reasons, reason, actor))

    if row.status == NluModelStatus.ACTIVE.value:
        if _in_sync(row, await _running_model_file()) is not False:  # đồng bộ (hoặc chưa biết vì Rasa bận): không có gì để làm
            raise _invalid(409, "already_active", f"{version} đang là bản chạy.")
        start(None, [])
        return {"version": version, "previous_version": previous, "forced": False, "gate_reasons": [], "status": "loading", "resync": True}

    reasons = decide_promotion({"eval_raw": row.eval_raw, "eval_final": row.eval_final}, {"eval_raw": active.eval_raw, "eval_final": active.eval_final} if active else None)
    if reasons and not force:
        raise _invalid(422, "blocked_by_gate", f"{version} không qua cổng đo; xem lý do, hoặc kích hoạt với force=true kèm lý do.", reasons)
    if reasons and len((reason or "").strip()) < MIN_REASON:
        raise _invalid(422, "reason_required", f"Ép kích hoạt bản không qua cổng đo phải ghi lý do (tối thiểu {MIN_REASON} ký tự).", reasons)
    source = directory / Path(row.artifact_path).name
    if not source.is_file():
        raise _invalid(404, "artifact_missing", f"Không thấy tệp model {source.name} trong thư mục model của Rasa.")
    staged = directory / (ACTIVE_FILE + ".tmp")
    shutil.copyfile(source, staged)  # chép trước: hỏng thì chưa đổi gì
    start(staged, reasons)
    return {"version": version, "previous_version": previous, "forced": bool(reasons), "gate_reasons": reasons, "status": "loading", "resync": False}
