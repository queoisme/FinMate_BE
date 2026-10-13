"""Đo độ chính xác tách món (tên + giá) trên 30 ảnh bill chuẩn, khởi đầu nguội (không dùng bộ nhớ cửa hàng).

Chạy trong container có model OCR, ví dụ:
  MSYS_NO_PATHCONV=1 docker run --rm -v "<ocr-service>:/app" -v "<ai-service/data-bill>:/data" -w /app \
      -e DATA_BILL=/data finmate-bill-api python benchmark/run_items.py [--verbose]

In ra hai số (đặc tả mục 4 "Đo độ chính xác"):
  (a) tỷ lệ items_ok = True trên các bill nhiều dòng hàng.
      Bill nhiều dòng = ảnh giấy có >= 2 dòng hàng có giá, ĐẾM BẰNG MẮT (expected_items.json, "_item_lines");
      ảnh chụp màn hình app/tin nhắn (null) bị loại vì không đi đường tách món.
  (b) trên các bill có nhãn tay VÀ items_ok = True: tỷ lệ món nhãn tay được ghép đúng giá.
      Món đúng = có một món tách ra (chưa dùng cho nhãn khác) cùng số tiền (đúng từng đồng) và tên gần giống
      name_contains (SequenceMatcher >= 0,6 trên một khúc tên, vì OCR đọc sai chữ). In kèm cách đo chặt (tên chứa
      nguyên đoạn) và chỉ so số tiền để không giấu số nào.
"""
import json
import os
import sys
from difflib import SequenceMatcher
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.stdout.reconfigure(encoding="utf-8")

from extract import _norm  # noqa: E402

HERE = Path(__file__).parent
DATA = Path(os.environ.get("DATA_BILL") or HERE.parents[1] / "data-bill")
EXPECTED = {k: v for k, v in json.loads((HERE / "expected_databill.json").read_text(encoding="utf-8")).items() if not k.startswith("_")}
_LABELS = json.loads((HERE / "expected_items.json").read_text(encoding="utf-8"))
ITEM_LINES = {k: v for k, v in _LABELS["_item_lines"].items() if not k.startswith("_")}
LABELS = {k: v for k, v in _LABELS.items() if not k.startswith("_")}
ADDED = set(_LABELS.get("_added_in_4b", {}).get("files", []))  # nhãn thêm sau khi sửa heuristic (bill mới ra items_ok)


FUZZY_RATIO = 0.6


def _fuzzy_in(frag: str, name: str) -> bool:
    """Đoạn nhãn gần giống một khúc cùng độ dài trong tên (OCR đọc sai chữ: "Sira chua" ~ "sua chua")."""
    name = _norm(name)
    n = len(frag)
    return any(SequenceMatcher(None, frag, name[i:i + n]).ratio() >= FUZZY_RATIO for i in range(max(1, len(name) - n + 1)))


def match_labels(labels: list[dict], items: list[dict], by_name: bool = True, fuzzy: bool = False) -> list[bool]:
    """Mỗi nhãn khớp nhiều nhất một món, mỗi món chỉ dùng cho một nhãn (hai món giống nhau phải tách ra hai món).

    Số tiền luôn so đúng từng đồng. Tên: mặc định tên phải chứa nguyên đoạn nhãn; fuzzy=True chấp nhận khúc tên
    giống đoạn nhãn từ FUZZY_RATIO trở lên (lỗi chính tả OCR); by_name=False bỏ qua tên, chỉ so số tiền.
    """
    def name_ok(frag: str, name: str) -> bool:
        return not by_name or (_fuzzy_in(frag, name) if fuzzy else frag in _norm(name))
    used, out = set(), []
    for lab in labels:
        hit = next((i for i, it in enumerate(items) if i not in used and it["amount"] == lab["amount"]
                    and name_ok(lab["name_contains"], it["name"])), None)
        if hit is not None:
            used.add(hit)
        out.append(hit is not None)
    return out


def process(path: Path) -> dict:
    """Giống main.analyze_bill (đọc hộp một lần, tổng qua analyze, tách món qua main._bill_items) nhưng không dùng bộ nhớ."""
    import app_screens
    import main
    from extract import analyze
    from items import payable_total
    from ocr import NoTextError, read_boxes

    try:
        boxes = read_boxes(path.read_bytes())
    except NoTextError:
        return {"total": None, "items": [], "ok": False, "screen": False}
    lines = [(b.text, b.conf) for b in boxes]
    total = analyze(lines)["total_amount"]
    screen = bool(app_screens.parse([t for t, _ in lines]))
    row_total = None if screen else payable_total(boxes)  # như main.analyze_bill
    if row_total is not None:
        total = row_total
    items, ok = ([], False) if screen else main._bill_items(boxes, total)
    return {"total": total, "items": items, "ok": ok, "screen": screen}


def main() -> None:
    verbose = "--verbose" in sys.argv
    multi = [n for n in EXPECTED if (ITEM_LINES.get(n) or 0) >= 2]
    ok_multi = 0
    cnt = {k: [0, 0] for k in ("fuzzy", "strict", "amount", "fuzzy_goc")}  # [đúng, tổng] trên bill có nhãn và items_ok
    print(f"ảnh lấy từ {DATA}\n")
    for name, exp in EXPECTED.items():
        r = process(DATA / name)
        is_multi = name in multi
        ok_multi += is_multi and r["ok"]
        s = sum(i["amount"] for i in r["items"])
        print(f"[{'OK ' if r['ok'] else 'sai'}] {name[:12]:12s} dòng(mắt)={ITEM_LINES.get(name)!s:4s} nhiều_dòng={'có ' if is_multi else 'không'} "
              f"tổng_đọc={r['total']} (đúng {exp['total']}) món={len(r['items'])} cộng_món={s}" + (" MÀN_HÌNH" if r["screen"] else ""))
        if name in LABELS:
            hits = match_labels(LABELS[name], r["items"], fuzzy=True)
            strict = match_labels(LABELS[name], r["items"])
            if r["ok"]:
                for k, h in (("fuzzy", hits), ("strict", strict), ("amount", match_labels(LABELS[name], r["items"], by_name=False))) \
                        + ((("fuzzy_goc", hits),) if name not in ADDED else ()):
                    cnt[k][0] += sum(h)
                    cnt[k][1] += len(h)
            print(f"      nhãn tay{' (thêm ở 4b)' if name in ADDED else ''}: {sum(hits)}/{len(hits)} món đúng (giá đúng, tên gần đúng)"
                  + ("" if r["ok"] else "  (items_ok=False, không tính vào (b))"))
            for lab, h, st in zip(LABELS[name], hits, strict):
                print(f"        {'✓' if h else '✗'}{'' if st else '~' if h else ' '} {lab['name_contains']!r} {lab['amount']}")
        if verbose or name in LABELS:
            for it in r["items"]:
                print(f"        -> {it['amount']:>10} {'GIẢM ' if it['is_discount'] else ''}{'(+ của #' + str(it['parent']) + ') ' if it['parent'] is not None else ''}{it['name']}")
    print(f"\n=== {len(EXPECTED)} ảnh chuẩn ===")
    print(f"(a) items_ok trên bill nhiều dòng (>=2 dòng hàng có giá, đếm bằng mắt, bỏ ảnh màn hình): "
          f"{ok_multi}/{len(multi)} ({ok_multi / len(multi):.0%})")
    def pct(k):
        a, n = cnt[k]
        return f"{a}/{n} ({a / n:.0%})" if n else "không có bill nào có nhãn mà items_ok=True"
    print(f"(b) món nhãn tay đúng trên bill có nhãn và items_ok=True (giá đúng từng đồng, tên gần đúng >= {FUZZY_RATIO}): {pct('fuzzy')}")
    print(f"    cùng (b) nhưng chỉ trên {len(LABELS) - len(ADDED)} bill gắn nhãn từ bước 4: {pct('fuzzy_goc')}")
    print(f"    tên chứa nguyên đoạn nhãn (cách đo ở bước 4): {pct('strict')}")
    print(f"    chỉ so số tiền (bỏ qua chữ tên): {pct('amount')}")
    print(f"    số bill có nhãn tay: {len(LABELS)} (thêm ở 4b: {len(ADDED)})")


if __name__ == "__main__":
    main()
