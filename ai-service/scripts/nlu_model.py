#!/usr/bin/env python3
"""Vòng đời model Rasa NLU: huấn luyện từ DB, đo, chặn model tệ hơn, chuyển model đang chạy. Sổ ghi ở bảng ``nlu_model_versions``.

Chạy từ ``ai-service`` (cần DB, Docker; ``DATABASE_URL`` trỏ ``localhost:5434`` khi chạy từ máy chủ):
    python scripts/nlu_model.py register finmate-nlu-v11 --path models/finmate-nlu-v11.tar.gz --active   (đăng ký model có sẵn, làm mốc)
    python scripts/nlu_model.py eval finmate-nlu-v11                                                       (đo trên tập test của DB, bật Rasa tạm cho model đó)
    python scripts/nlu_model.py train finmate-nlu-v12                                                      (xuất DB -> huấn luyện -> ghi candidate kèm mã băm dữ liệu)
    python scripts/nlu_model.py eval finmate-nlu-v12
    python scripts/nlu_model.py promote finmate-nlu-v12                                                    (bị chặn nếu tệ hơn bản đang chạy)
    python scripts/nlu_model.py list | reject <version> --note "lý do"

Hai bộ số đo cho cùng một câu: ``eval_raw`` = Rasa thô, ``eval_final`` = Rasa + lớp luật của ``transaction_parser`` (thứ người dùng thấy). Luật che lỗi của model,
nên cổng so CẢ HAI: model không được tốt lên ở số cuối chỉ vì luật gánh. Đo trong tiến trình như ``audit_overrides.py`` (cùng ``intent_of``).
"""
from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from sqlalchemy import select, update  # noqa: E402

from app.core.enums import NluModelStatus, SplitName  # noqa: E402
from app.db.models.nlu_example import NluExample, plain_text  # noqa: E402
from app.db.models.nlu_model_version import NluModelVersion  # noqa: E402
from app.db.session import AsyncSessionLocal  # noqa: E402
from app.services.nlu import RasaNlu  # noqa: E402
from app.services.nlu_models import INDEPENDENT_TAGS, decide_promotion  # noqa: E402,F401 - cổng đo dùng chung với API trang admin (app/services/nlu_models.py)

RASA_DIR = ROOT / "rasa"
COMPOSE_DIR = ROOT.parent  # docker-compose.yml nằm ở gốc repo
ACTIVE_FILE = "models/active.tar.gz"  # docker-compose.yml chạy `-m models/active.tar.gz`; promote chép model vào đây rồi khởi động lại Rasa
RASA_IMAGE = os.environ.get("RASA_IMAGE", "rasa/rasa:3.6.21")
EVAL_PORT = int(os.environ.get("NLU_EVAL_PORT", "5006"))
WRONG_CONFIDENT, WRONG_SATURATED = 0.8, 0.97  # cùng ngưỡng với rasa/tools/eval_live.py: "sai mà chắc" và "sai mà bão hòa"


# ------------------------------------------------------------------ logic thuần (test không cần DB/Rasa)

def _acc(ok: int, n: int) -> float:
    return round(ok / n, 4) if n else 0.0


def hash_of_test_set(rows: list[NluExample]) -> str:
    """Mã băm tập test: hai số đo chỉ so được với nhau khi cùng tập test."""
    lines = sorted(f"{r.intent}\t{r.text_key}" for r in rows)
    return hashlib.sha256("\n".join(lines).encode("utf-8")).hexdigest()


def summarize(results: list[dict], test_hash: str) -> tuple[dict, dict]:
    """results: [{tag, label, raw, conf, final}] -> (eval_raw, eval_final)."""

    def block(key: str) -> dict:
        per: dict[str, dict] = {}
        by_tag: dict[str, dict] = {}
        for r in results:
            ok = r[key] == r["label"]
            for bucket, name in ((per, r["label"]), (by_tag, r["tag"])):
                cell = bucket.setdefault(name, {"n": 0, "ok": 0})
                cell["n"] += 1
                cell["ok"] += ok
        n, ok = len(results), sum(r[key] == r["label"] for r in results)
        return {
            "test_hash": test_hash, "n": n, "acc": _acc(ok, n), "per_intent": per,
            "by_tag": {tag: {**cell, "acc": _acc(cell["ok"], cell["n"])} for tag, cell in by_tag.items()},
        }

    raw = block("raw")
    raw["wrong_confident"] = sum(r["raw"] != r["label"] and r["conf"] >= WRONG_CONFIDENT for r in results)
    raw["wrong_saturated"] = sum(r["raw"] != r["label"] and r["conf"] >= WRONG_SATURATED for r in results)
    final = block("final")
    over = [r for r in results if r["final"] != r["raw"]]
    final["overrides"] = len(over)
    final["fixed"] = sum(r["final"] == r["label"] for r in over)  # cứu: Rasa sai, luật đúng
    final["broke"] = sum(r["raw"] == r["label"] and r["final"] != r["label"] for r in over)  # phá: Rasa đúng, luật sai
    return raw, final


# ------------------------------------------------------------------ đo

def _post_json(url: str, payload: dict, timeout: float = 10) -> dict:
    request = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read())


def serve_for_eval(version: str, artifact: str) -> str:
    """Bật một container Rasa TẠM cho đúng model này (không đụng container đang chạy). Trả URL; ``stop_eval_server`` dọn."""
    name = f"nlu-eval-{version}"
    subprocess.run(["docker", "rm", "-f", name], capture_output=True)
    subprocess.run(
        ["docker", "run", "-d", "--rm", "--name", name, "-p", f"{EVAL_PORT}:5005", "-v", f"{RASA_DIR / 'models'}:/app/models:ro",
         RASA_IMAGE, "run", "--enable-api", "-m", artifact, "--port", "5005"],
        check=True, capture_output=True,
    )
    url = f"http://localhost:{EVAL_PORT}"
    deadline = time.time() + 180
    while time.time() < deadline:
        try:
            _post_json(f"{url}/model/parse", {"text": "ăn phở 45k"}, timeout=5)
            return url
        except Exception:  # noqa: BLE001 - Rasa đang nạp model
            time.sleep(3)
    raise SystemExit(f"Rasa tạm cho {version} không lên sau 180 giây (docker logs {name})")


def stop_eval_server(version: str) -> None:
    subprocess.run(["docker", "stop", f"nlu-eval-{version}"], capture_output=True)


async def collect(rasa_url: str, rows: list[NluExample]) -> list[dict]:
    from audit_overrides import intent_of  # cùng phép đo với cổng cứu/phá: parse_chat với `now` cố định, needs_amount tính là add_expense

    client = RasaNlu(rasa_url, timeout=10)
    out = []
    for row in rows:
        text = plain_text(row.text)  # người dùng gõ câu thường, không có chú thích thực thể
        nlu = await client.parse(text)
        if nlu is None:
            raise SystemExit(f"Rasa không trả lời ở {rasa_url}")
        out.append({"tag": row.tag or "-", "label": row.intent, "raw": nlu["intent"], "conf": nlu["confidence"], "final": intent_of(text, nlu)})
    return out


async def _row(session, version: str) -> NluModelVersion:
    row = await session.scalar(select(NluModelVersion).where(NluModelVersion.version == version))
    if row is None:
        raise SystemExit(f"Chưa có model {version} trong sổ. `list` để xem, `register`/`train` để thêm.")
    return row


# ------------------------------------------------------------------ lệnh

async def cmd_register(args: argparse.Namespace) -> int:
    path = RASA_DIR / args.path
    if not path.is_file():
        raise SystemExit(f"Không thấy {path}")
    async with AsyncSessionLocal() as session:
        if await session.scalar(select(NluModelVersion).where(NluModelVersion.version == args.version)):
            raise SystemExit(f"{args.version} đã có trong sổ")
        if args.active and await session.scalar(select(NluModelVersion).where(NluModelVersion.status == NluModelStatus.ACTIVE.value)):
            raise SystemExit("Đã có model active; dùng `promote`")
        session.add(NluModelVersion(
            version=args.version, artifact_path=args.path, dataset_hash="unknown", notes=args.note or "đăng ký model có sẵn: không rõ bản dữ liệu đã học",
            status=(NluModelStatus.ACTIVE if args.active else NluModelStatus.CANDIDATE).value, activated_at=datetime.now(timezone.utc) if args.active else None,
        ))
        await session.commit()
    if args.active and not (RASA_DIR / ACTIVE_FILE).is_file():
        shutil.copyfile(path, RASA_DIR / ACTIVE_FILE)  # docker-compose.yml chạy models/active.tar.gz
    print(f"Đã đăng ký {args.version}" + (" (đang chạy)" if args.active else ""))
    return 0


async def cmd_train(args: argparse.Namespace) -> int:
    from nlu_examples import cmd_export_all, snapshot

    async with AsyncSessionLocal() as session:
        if await session.scalar(select(NluModelVersion).where(NluModelVersion.version == args.version)):
            raise SystemExit(f"{args.version} đã có trong sổ")
    await cmd_export_all(argparse.Namespace(dir=str(RASA_DIR)))  # tệp train đúng bằng DB lúc này
    digest, counts = await snapshot()
    subprocess.run(
        ["docker", "compose", "run", "--rm", "rasa", "train", "nlu", "--nlu", "data", "--config", "config.yml", "--domain", "domain.yml", "--out", "models",
         "--fixed-model-name", args.version],
        cwd=COMPOSE_DIR, check=True,
    )
    artifact = f"models/{args.version}.tar.gz"
    if not (RASA_DIR / artifact).is_file():
        raise SystemExit(f"Huấn luyện xong mà không thấy {RASA_DIR / artifact}")
    async with AsyncSessionLocal() as session:
        session.add(NluModelVersion(version=args.version, artifact_path=artifact, status=NluModelStatus.CANDIDATE.value, dataset_hash=digest, dataset_counts=counts))
        await session.commit()
    print(f"Đã huấn luyện {args.version} (dữ liệu {digest[:12]}, {sum(counts.get('train', {}).values())} câu train). Chạy `eval {args.version}`.")
    return 0


async def cmd_eval(args: argparse.Namespace) -> int:
    from nlu_examples import _fetch

    async with AsyncSessionLocal() as session:
        row = await _row(session, args.version)
        artifact = row.artifact_path
    rows = await _fetch(SplitName.TEST.value)
    if not rows:
        raise SystemExit("Tập test của DB rỗng")
    served = args.rasa_url is None
    url = args.rasa_url or serve_for_eval(args.version, artifact)
    try:
        results = await collect(url, rows)
    finally:
        if served:
            stop_eval_server(args.version)
    raw, final = summarize(results, hash_of_test_set(rows))
    async with AsyncSessionLocal() as session:
        await session.execute(update(NluModelVersion).where(NluModelVersion.version == args.version).values(eval_raw=raw, eval_final=final))
        await session.commit()
    print(f"{args.version} trên {raw['n']} câu test:  Rasa thô {raw['acc']:.2%} (sai mà chắc ≥0,8: {raw['wrong_confident']}, ≥0,97: {raw['wrong_saturated']})  |  "
          f"Rasa + luật {final['acc']:.2%} (luật ghi đè {final['overrides']}: cứu {final['fixed']}, phá {final['broke']})")
    for tag, cell in sorted(final["by_tag"].items()):
        print(f"  {tag:28} {cell['ok']:4}/{cell['n']:<4} sau luật {cell['acc']:.2%}   thô {raw['by_tag'][tag]['acc']:.2%}")
    return 0


async def switch_active(session, row: NluModelVersion, active: NluModelVersion | None) -> None:
    """Bản cũ nghỉ rồi bản mới lên, trong một giao dịch."""
    if active:
        active.status = NluModelStatus.RETIRED.value
        await session.flush()  # hạ bản cũ trước: index duy nhất chỉ cho một dòng active
    row.status, row.activated_at = NluModelStatus.ACTIVE.value, datetime.now(timezone.utc)
    await session.commit()


async def cmd_promote(args: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        row = await _row(session, args.version)
        if row.status != NluModelStatus.CANDIDATE.value:
            raise SystemExit(f"{args.version} đang ở trạng thái {row.status}; chỉ candidate mới promote được")
        active = await session.scalar(select(NluModelVersion).where(NluModelVersion.status == NluModelStatus.ACTIVE.value))
        reasons = decide_promotion(
            {"eval_raw": row.eval_raw, "eval_final": row.eval_final},
            {"eval_raw": active.eval_raw, "eval_final": active.eval_final} if active else None,
        )
        if reasons:
            print(f"KHÔNG promote {args.version}:")
            for reason in reasons:
                print(f"  - {reason}")
            return 1
        if args.dry_run:
            print(f"{args.version} qua cổng: promote sẽ chuyển model đang chạy sang bản này (chưa đổi gì).")
            return 0
        source = RASA_DIR / row.artifact_path
        staged = RASA_DIR / (ACTIVE_FILE + ".tmp")
        shutil.copyfile(source, staged)  # chép trước: hỏng thì chưa đổi gì trong DB
        await switch_active(session, row, active)
    os.replace(staged, RASA_DIR / ACTIVE_FILE)
    print(f"{args.version} đang chạy (đã chép vào {ACTIVE_FILE}).")
    if not args.no_restart:
        subprocess.run(["docker", "compose", "restart", "rasa"], cwd=COMPOSE_DIR, check=True)
        print("Đã khởi động lại Rasa; ai-service dùng luật tới ~30 giây (cầu dao) rồi gọi lại Rasa.")
    return 0


async def cmd_reject(args: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        row = await _row(session, args.version)
        if row.status != NluModelStatus.CANDIDATE.value:
            raise SystemExit(f"chỉ candidate mới reject được (đang {row.status})")
        row.status, row.notes = NluModelStatus.REJECTED.value, args.note
        await session.commit()
    print(f"Đã loại {args.version}")
    return 0


async def cmd_list(_: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        rows = (await session.scalars(select(NluModelVersion).order_by(NluModelVersion.created_at))).all()
    for r in rows:
        raw, final = (r.eval_raw or {}).get("acc"), (r.eval_final or {}).get("acc")
        measured = f"thô {raw:.2%} / sau luật {final:.2%}" if raw is not None and final is not None else "chưa đo"
        print(f"{r.version:24} {r.status:10} dữ liệu {r.dataset_hash[:12]:12} {measured}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("register")
    p.add_argument("version")
    p.add_argument("--path", required=True, help="tương đối so với rasa/, vd. models/finmate-nlu-v11.tar.gz")
    p.add_argument("--active", action="store_true", help="model này đang chạy (làm mốc)")
    p.add_argument("--note")
    sub.add_parser("train").add_argument("version")
    p = sub.add_parser("eval")
    p.add_argument("version")
    p.add_argument("--rasa-url", help="dùng một Rasa đang chạy model này; bỏ trống thì bật container tạm")
    p = sub.add_parser("promote")
    p.add_argument("version")
    p.add_argument("--no-restart", action="store_true")
    p.add_argument("--dry-run", action="store_true", help="chỉ chạy cổng chặn và in kết quả")
    p = sub.add_parser("reject")
    p.add_argument("version")
    p.add_argument("--note", required=True)
    sub.add_parser("list")
    args = parser.parse_args()
    handlers = {"register": cmd_register, "train": cmd_train, "eval": cmd_eval, "promote": cmd_promote, "reject": cmd_reject, "list": cmd_list}
    return asyncio.run(handlers[args.cmd](args))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
