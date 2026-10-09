"""Đo độ chính xác tách món (tên + giá) trên 30 ảnh bill chuẩn, khởi đầu nguội (không dùng bộ nhớ cửa hàng).

Chạy trong container có model OCR, ví dụ:
  MSYS_NO_PATHCONV=1 docker run --rm -v "<ocr-service>:/app" -v "<ai-service/data-bill>:/data" -w /app \
      -e DATA_BILL=/data finmate-bill-api python benchmark/run_items.py [--verbose]

In ra hai số (đặc tả mục 4 "Đo độ chính xác"):
  (a) tỷ lệ items_ok = True trên các bill nhiều dòng hàng.
      Bill nhiều dòng = ảnh giấy có >= 2 dòng hàng có giá, ĐẾM BẰNG MẮT (expected_items.json, "_item_lines");
      ảnh chụp màn hình app/tin nhắn (null) bị loại vì không đi đường tách món.
  (b) trên các bill có nhãn tay VÀ items_ok = True: tỷ lệ món nhãn tay được ghép đúng giá.
      Món đúng = có một món tách ra (chưa dùng cho nhãn khác) cùng số tiền và tên chứa name_contains.
"""
import json
import os
import sys
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


def match_labels(labels: list[dict], items: list[dict], by_name: bool = True) -> list[bool]:
    """Mỗi nhãn khớp nhiều nhất một món, mỗi món chỉ dùng cho một nhãn (hai món giống nhau phải tách ra hai món).

    by_name=False chỉ so số tiền: tách bạch lỗi ghép giá với lỗi OCR đọc sai chữ trong tên ("Sira" thay "Sữa").
    """
    used, out = set(), []
    for lab in labels:
        hit = next((i for i, it in enumerate(items) if i not in used and it["amount"] == lab["amount"]
                    and (not by_name or lab["name_contains"] in _norm(it["name"]))), None)
        if hit is not None:
            used.add(hit)
        out.append(hit is not None)
    return out


def process(path: Path) -> dict:
    """Giống main.analyze_bill (đọc hộp một lần, tổng qua analyze, tách món qua main._bill_items) nhưng không dùng bộ nhớ."""
    import app_screens
    import main
    from extract import analyze
    from ocr import NoTextError, read_boxes

    try:
        boxes = read_boxes(path.read_bytes())
    except NoTextError:
        return {"total": None, "items": [], "ok": False, "screen": False}
    lines = [(b.text, b.conf) for b in boxes]
    total = analyze(lines)["total_amount"]
    screen = bool(app_screens.parse([t for t, _ in lines]))
    items, ok = ([], False) if screen else main._bill_items(boxes, total)
    return {"total": total, "items": items, "ok": ok, "screen": screen}


def main() -> None:
    verbose = "--verbose" in sys.argv
    multi = [n for n in EXPECTED if (ITEM_LINES.get(n) or 0) >= 2]
    ok_multi, lab_hit, lab_n, amt_hit = 0, 0, 0, 0
    print(f"ảnh lấy từ {DATA}\n")
    for name, exp in EXPECTED.items():
        r = process(DATA / name)
        is_multi = name in multi
        ok_multi += is_multi and r["ok"]
        s = sum(i["amount"] for i in r["items"])
        print(f"[{'OK ' if r['ok'] else 'sai'}] {name[:12]:12s} dòng(mắt)={ITEM_LINES.get(name)!s:4s} nhiều_dòng={'có ' if is_multi else 'không'} "
              f"tổng_đọc={r['total']} (đúng {exp['total']}) món={len(r['items'])} cộng_món={s}" + (" MÀN_HÌNH" if r["screen"] else ""))
        if name in LABELS:
            hits = match_labels(LABELS[name], r["items"])
            if r["ok"]:
                lab_hit, lab_n = lab_hit + sum(hits), lab_n + len(hits)
                amt_hit += sum(match_labels(LABELS[name], r["items"], by_name=False))
            print(f"      nhãn tay: {sum(hits)}/{len(hits)} món đúng giá" + ("" if r["ok"] else "  (items_ok=False, không tính vào (b))"))
            for lab, h in zip(LABELS[name], hits):
                print(f"        {'✓' if h else '✗'} {lab['name_contains']!r} {lab['amount']}")
        if verbose or name in LABELS:
            for it in r["items"]:
                print(f"        -> {it['amount']:>10} {'GIẢM ' if it['is_discount'] else ''}{'(+ của #' + str(it['parent']) + ') ' if it['parent'] is not None else ''}{it['name']}")
    print(f"\n=== {len(EXPECTED)} ảnh chuẩn ===")
    print(f"(a) items_ok trên bill nhiều dòng (>=2 dòng hàng có giá, đếm bằng mắt, bỏ ảnh màn hình): "
          f"{ok_multi}/{len(multi)} ({ok_multi / len(multi):.0%})")
    print(f"(b) món nhãn tay đúng giá trên bill có nhãn và items_ok=True: "
          + (f"{lab_hit}/{lab_n} ({lab_hit / lab_n:.0%})" if lab_n else "không có bill nào có nhãn mà items_ok=True"))
    if lab_n:
        print(f"    chỉ so số tiền (bỏ qua chữ tên): {amt_hit}/{lab_n} ({amt_hit / lab_n:.0%})")
    print(f"    số bill có nhãn tay: {len(LABELS)}")


if __name__ == "__main__":
    main()
