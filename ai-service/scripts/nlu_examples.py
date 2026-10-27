#!/usr/bin/env python3
"""Câu mẫu của Rasa NLU nằm trong DB (bảng ``nlu_examples``): nhập, xuất YAML để huấn luyện, đo theo nhóm, thống kê.

Chạy trong container ai-service (có DB và Rasa):
    python scripts/nlu_examples.py import seed/nlu_redteam_oos.jsonl            (xem trước, không ghi gì)
    python scripts/nlu_examples.py import seed/nlu_redteam_oos.jsonl --write    (ghi vào DB)
    python scripts/nlu_examples.py import rasa_cu/nlu.yml --split train --tag handwritten [--write]   (tệp YAML của Rasa: chỉ cần khi nhập dữ liệu cũ)
    python scripts/nlu_examples.py stats
    python scripts/nlu_examples.py eval [--split test] [--tag ai_tech]          (đo qua POST /chat/parse: Rasa + luật, đúng thứ người dùng thấy)
    python scripts/nlu_examples.py export --split train --out rasa/data/nlu_db.yml
    python scripts/nlu_examples.py export-all [--dir rasa]                      (data/nlu_db.yml = tập train; tests/nlu_test*.yml = từng nhóm test; huấn luyện Rasa từ DB chỉ cần lệnh này)

Mỗi dòng JSONL: {"text": "...", "intent": "out_of_scope", "tag": "ai_tech"}; "split" và "source" là tùy chọn. Thiếu "split" thì gán TẤT ĐỊNH bằng băm của câu
(1/3 vào test, còn lại train): một câu nằm mãi ở một tập, không rò sang bên kia sau mỗi lần nhập. Tệp JSONL/YAML chỉ là đầu vào nhập; sau đó DB là nguồn sự thật.
Câu có thể mang chú thích thực thể của Rasa ("ăn phở [45k](amount)"; thực thể hợp lệ: amount, merchant, date); khóa so trùng và phép đo dùng câu đã bỏ chú thích.
Câu đã có thì bỏ qua; câu đã có với intent KHÁC thì báo xung đột và không ghi đè (muốn đổi thì sửa dòng trong DB: đặt is_active = false rồi nhập lại).
"""

from __future__ import annotations

import argparse
import asyncio
import collections
import hashlib
import json
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import yaml  # noqa: E402
from sqlalchemy import select  # noqa: E402

from app.core.enums import LabelSource, SplitName  # noqa: E402
from app.db.models.nlu_example import MAX_TEXT_CHARS, NluExample, annotation_problem, default_split, plain_text, text_key  # noqa: E402
from app.db.session import AsyncSessionLocal  # noqa: E402

_INTENT_RE = re.compile(r"^[a-z][a-z_]{1,31}$")


def problem(row: dict) -> str | None:
    text, intent = row.get("text"), row.get("intent")
    if not isinstance(text, str) or not text.strip():
        return "thiếu text"
    if len(text) > MAX_TEXT_CHARS or "\n" in text:
        return f"text phải một dòng, tối đa {MAX_TEXT_CHARS} ký tự"
    if reason := annotation_problem(text):
        return reason
    if not isinstance(intent, str) or not _INTENT_RE.match(intent):
        return "intent phải là tên chữ thường kiểu out_of_scope"
    if row.get("split", SplitName.TRAIN.value) not in SplitName.values():
        return f"split phải là một trong {SplitName.values()}"
    if row.get("source", LabelSource.SEED.value) not in LabelSource.values():
        return f"source phải là một trong {LabelSource.values()}"
    return None


def read_rows(path: pathlib.Path, split: str | None, tag: str | None) -> list[dict]:
    """JSONL (mỗi dòng một câu) hoặc YAML của Rasa (``nlu: - intent / examples``). YAML bắt buộc ``--split`` vì tệp không nói câu thuộc tập nào."""
    if path.suffix.lower() not in (".yml", ".yaml"):
        return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if split is None:
        raise SystemExit(f"{path.name}: tệp YAML cần --split train|val|test")
    rows = []
    for block in yaml.safe_load(path.read_text(encoding="utf-8")).get("nlu", []):
        if "intent" not in block:
            continue  # synonym / regex / lookup: không phải câu mẫu
        examples = block.get("examples") or ""
        lines = examples.splitlines() if isinstance(examples, str) else [str(e) for e in examples]
        rows += [{"text": line.strip().removeprefix("- ").strip(), "intent": block["intent"], "split": split, "tag": tag or path.stem}
                 for line in lines if line.strip().removeprefix("- ").strip()]
    return rows


async def cmd_import(args: argparse.Namespace) -> int:
    rows: dict[str, dict] = {}
    skipped = collections.Counter()
    read = 0
    for file in args.file:
        for number, row in enumerate(read_rows(pathlib.Path(file), args.split, args.tag), 1):
            read += 1
            if reason := problem(row):
                print(f"  {pathlib.Path(file).name} #{number} bị bỏ: {reason}: {str(row.get('text'))[:80]}")
                skipped["lỗi"] += 1
                continue
            key = text_key(row["text"])
            row = {**row, "split": row.get("split") or default_split(key), "source": row.get("source", LabelSource.SEED.value)}
            if key in rows:
                prior = rows[key]
                leak = prior["split"] != row["split"] or prior["intent"] != row["intent"]
                if leak:
                    print(f"  TRÙNG KHÁC TẬP/NHÃN {plain_text(row['text'])!r}: {prior['split']}/{prior['intent']} và {row['split']}/{row['intent']} (giữ cái đầu)")
                skipped["trùng khác tập/nhãn" if leak else "trùng trong tệp"] += 1
                continue
            rows[key] = row
    async with AsyncSessionLocal() as session:
        existing = {r.text_key: r for r in (await session.scalars(select(NluExample).where(NluExample.text_key.in_(list(rows))))).all()}
        new = []
        for key, row in rows.items():
            old = existing.get(key)
            if old is None:
                new.append(NluExample(text=row["text"].strip(), text_key=key, intent=row["intent"], split=row["split"], tag=row.get("tag"), source=row["source"]))
            elif old.intent != row["intent"]:
                print(f"  XUNG ĐỘT {plain_text(row['text'])!r}: DB là {old.intent}, tệp là {row['intent']} (không ghi đè)")
                skipped["xung đột"] += 1
            elif old.split != row["split"]:
                print(f"  RÒ RỈ {plain_text(row['text'])!r}: DB đã có ở tập {old.split}, tệp muốn {row['split']} (không đổi)")
                skipped["đã có ở tập khác"] += 1
            else:
                skipped["đã có"] += 1
        by = collections.Counter((r.split, r.tag) for r in new)
        print(f"{read} câu đọc, {len(new)} câu mới, bỏ qua: {dict(skipped) or 0}.")
        for (split, tag), count in sorted(by.items(), key=lambda item: (item[0][0], str(item[0][1]))):
            print(f"  {split:5} {str(tag):20} {count}")
        if not args.write:
            print("Xem trước thôi. Thêm --write để ghi vào DB.")
            return 0
        session.add_all(new)
        await session.commit()
        print("Đã ghi.")
    return 0


async def _fetch(split: str, tag: str | None = None, intent: str | None = None) -> list[NluExample]:
    query = select(NluExample).where(NluExample.is_active, NluExample.split == split).order_by(NluExample.intent, NluExample.text_key)
    if tag:
        query = query.where(NluExample.tag == tag)
    if intent:
        query = query.where(NluExample.intent == intent)
    async with AsyncSessionLocal() as session:
        return list((await session.scalars(query)).all())


def to_yaml(rows: list[NluExample], label: str) -> str:
    out = ['version: "3.1"', "", f"# Sinh bởi scripts/nlu_examples.py từ bảng nlu_examples ({label}). KHÔNG sửa tay: sửa trong DB.", "", "nlu:"]
    for intent in sorted({r.intent for r in rows}):
        out += [f"- intent: {intent}", "  examples: |"] + [f"    - {r.text}" for r in rows if r.intent == intent] + [""]
    return "\n".join(out) + "\n"


async def cmd_export(args: argparse.Namespace) -> int:
    rows = await _fetch(args.split, tag=args.tag, intent=args.intent)
    text = to_yaml(rows, f"tập {args.split}")
    if args.out:
        pathlib.Path(args.out).write_text(text, encoding="utf-8", newline="\n")  # ghi thẳng UTF-8: `>` của PowerShell ghi sai mã hóa
        print(f"{len(rows)} câu -> {args.out}")
    else:
        sys.stdout.write(text)
    return 0


def snapshot_of(rows: list[NluExample]) -> tuple[str, dict[str, dict[str, int]]]:
    """(mã băm tập train, số câu theo tập và ý định). Mã băm chỉ phụ thuộc NỘI DUNG (ý định + câu có chú thích), không phụ thuộc thứ tự dòng hay nhãn nhóm,
    nên hai lần xuất cùng dữ liệu cho cùng mã: ghi cạnh model để biết nó học từ bản dữ liệu nào."""
    train = sorted((r.intent, r.text_key, r.text) for r in rows if r.split == SplitName.TRAIN.value)
    digest = hashlib.sha256("\n".join(f"{intent}\t{text}" for intent, _, text in train).encode("utf-8")).hexdigest()
    counts: dict[str, dict[str, int]] = {}
    for r in rows:
        bucket = counts.setdefault(r.split, {})
        bucket[r.intent] = bucket.get(r.intent, 0) + 1
    return digest, counts


async def snapshot() -> tuple[str, dict[str, dict[str, int]]]:
    async with AsyncSessionLocal() as session:
        return snapshot_of(list((await session.scalars(select(NluExample).where(NluExample.is_active))).all()))


def test_file_name(tag: str | None) -> str:
    """Nhóm test -> tên tệp trong rasa/tests. Giữ tên cũ (nlu_test_fresh2...) để audit_overrides.py / eval_live.py / `rasa test nlu` tìm thấy; nhóm khác có tiền tố nlu_test_db_."""
    if not tag:
        return "nlu_test_db.yml"
    return f"{tag}.yml" if tag.startswith("nlu_test") else f"nlu_test_db_{tag}.yml"


async def cmd_export_all(args: argparse.Namespace) -> int:
    """Mọi thứ Rasa cần đọc từ tệp, sinh từ DB: tập train -> data/nlu_db.yml; mỗi nhóm test -> tests/nlu_test*.yml."""
    root = pathlib.Path(args.dir)
    train = await _fetch(SplitName.TRAIN.value)
    (root / "data").mkdir(parents=True, exist_ok=True)
    (root / "data" / "nlu_db.yml").write_text(to_yaml(train, "tập train"), encoding="utf-8", newline="\n")
    print(f"data/nlu_db.yml: {len(train)} câu")
    test = await _fetch(SplitName.TEST.value)
    (root / "tests").mkdir(parents=True, exist_ok=True)
    for tag in sorted({r.tag for r in test}, key=str):
        group = [r for r in test if r.tag == tag]
        name = test_file_name(tag)
        (root / "tests" / name).write_text(to_yaml(group, f"tập test, nhóm {tag}"), encoding="utf-8", newline="\n")
        print(f"tests/{name}: {len(group)} câu")
    return 0


async def cmd_eval(args: argparse.Namespace) -> int:
    from eval_nlu_pipeline import call  # cùng đường gọi với scorecard: POST /chat/parse, allow_ai=False

    rows = await _fetch(args.split, tag=args.tag)
    if not rows:
        print("Không có câu nào trong tập này.")
        return 1
    tally: dict[str, collections.Counter] = collections.defaultdict(collections.Counter)
    bad = []
    via_rasa = 0
    for row in rows:
        out = call(plain_text(row.text))  # người dùng gõ câu thường, không có chú thích thực thể
        via_rasa += out["nlu"].get("source") == "rasa"
        tally[row.tag or "-"]["n"] += 1
        if out["intent"] == row.intent:
            tally[row.tag or "-"]["ok"] += 1
        else:
            bad.append(f"[{row.tag}] {plain_text(row.text)!r}: muốn {row.intent}, ra {out['intent']} (Rasa {out['nlu'].get('intent')} {out['nlu'].get('confidence')})")
    total, ok = sum(t["n"] for t in tally.values()), sum(t["ok"] for t in tally.values())
    print(f"tập {args.split}: {ok}/{total} đúng ({ok / total:.1%}), rò rỉ {total - ok}; Rasa quyết {via_rasa}/{total}, luật quyết {total - via_rasa}")
    for tag, t in sorted(tally.items()):
        print(f"  {tag:20} {t['ok']:3}/{t['n']:<3} {t['ok'] / t['n']:.1%}")
    for line in bad:
        print("  -", line)
    if via_rasa < total // 2:  # gần như không câu nào do Rasa quyết = Rasa nguội/tắt (cầu dao 30 giây mở sau khi khởi động lại), không phải Rasa không chắc
        print("CẢNH BÁO: Rasa gần như không tham gia (vừa khởi động lại? cầu dao đang mở?). Đợi một phút rồi chạy lại; số trên là của riêng lớp luật.")
        return 2
    return 0


async def cmd_stats(_: argparse.Namespace) -> int:
    async with AsyncSessionLocal() as session:
        rows = (await session.scalars(select(NluExample))).all()
    count = collections.Counter((r.split, r.intent, r.tag, r.is_active) for r in rows)
    print(f"{len(rows)} câu")
    for (split, intent, tag, active), n in sorted(count.items(), key=lambda item: tuple(str(x) for x in item[0])):
        print(f"  {split:5} {intent:16} {str(tag):20} {'' if active else '(đã tắt) '}{n}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("import")
    p.add_argument("file", nargs="+")
    p.add_argument("--write", action="store_true")
    p.add_argument("--split", choices=SplitName.values(), help="bắt buộc với tệp YAML")
    p.add_argument("--tag", help="mặc định (YAML): tên tệp, vd. nlu_test_fresh2")
    p = sub.add_parser("export")
    p.add_argument("--split", default=SplitName.TRAIN.value, choices=SplitName.values())
    p.add_argument("--intent")
    p.add_argument("--tag")
    p.add_argument("--out")
    p = sub.add_parser("export-all")
    p.add_argument("--dir", default=str(pathlib.Path(__file__).resolve().parent.parent / "rasa"))
    p = sub.add_parser("eval")
    p.add_argument("--split", default=SplitName.TEST.value, choices=SplitName.values())
    p.add_argument("--tag")
    sub.add_parser("stats")
    args = parser.parse_args()
    return asyncio.run({"import": cmd_import, "export": cmd_export, "export-all": cmd_export_all, "eval": cmd_eval, "stats": cmd_stats}[args.cmd](args))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
