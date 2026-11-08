"""Sinh ``seed/nlu_purchase.jsonl``: câu cho ý định ``evaluate_purchase`` ("Hôm nay mua cái quần 800.000đ được không?") và các câu ĐỐI NGHỊCH dễ lẫn với nó.

Chỉ là máy sinh ỨNG VIÊN (cùng kiểu ``generate_periods.py``): nó ghi JSONL, rồi nhập vào DB bằng ``scripts/nlu_examples.py import seed/nlu_purchase.jsonl --write``;
DB mới là nguồn sự thật.

- TRAIN và TEST dùng KHUNG CÂU KHÁC NHAU (không chia ngẫu nhiên theo băm), để tập test (tag ``purchase_eval``) đo khả năng khái quát chứ không đo việc thuộc lòng.
- Tự kiểm bằng chính ``parse_purchase``: mọi câu ``evaluate_purchase`` phải ra bản nháp, mọi câu đối nghịch KHÔNG được ra gì, để nhãn của dữ liệu không mâu thuẫn
  với lớp luật (nhãn dạy một đằng, luật quyết một nẻo thì ``audit_overrides`` đo sai).
- Chỉ dùng cách hỏi KHẢ NĂNG CHI ("được không", "ổn không", "đủ tiền"...). Cách hỏi xin lời khuyên ("có nên", "đáng mua không") đã là ``out_of_scope`` trong DB.
- Câu trùng bộ độc lập ``tests/fixtures/eval/rasa_coverage.jsonl`` bị loại.

Chạy từ ``ai-service``:  ``PYTHONPATH=. python rasa/tools/generate_purchase.py``
"""
from __future__ import annotations

import json
import pathlib
import random
import re
import unicodedata
from datetime import datetime

from app.services.purchase_parser import parse_purchase
from app.services.transaction_parser import VN

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "seed" / "nlu_purchase.jsonl"
FIXTURE = ROOT / "tests" / "fixtures" / "eval" / "rasa_coverage.jsonl"
NOW = datetime(2026, 11, 6, 10, 0, tzinfo=VN)  # mọi ngày bên dưới đều từ hôm nay trở đi
SEED = 20261106
UNACCENTED_SHARE = 0.35

ITEMS = ["cái quần", "áo khoác", "đôi giày", "laptop", "điện thoại", "tai nghe", "xe máy", "tủ lạnh", "máy giặt", "ghế gaming", "vé concert", "balo", "đồng hồ",
         "máy ảnh", "bàn phím cơ", "nồi chiên không dầu", "màn hình", "ipad", "sofa", "cái giường", "máy lọc không khí", "loa bluetooth", "váy", "túi xách",
         "xe đạp", "bộ bàn ăn", "máy chơi game", "tivi", "quạt điều hòa", "kính mát"]
PRICES = ["800k", "350k", "1tr2", "2 triệu", "15 triệu", "20tr", "500.000đ", "1,5 triệu", "12 triệu", "45tr", "3 củ", "700 nghìn", "2tr5", "6 triệu", "90k", "25 triệu"]
DATES_TRAIN = ["hôm nay", "ngày mai", "ngày kia", "ngày 16", "ngày 20/11", "ngày 25 tháng 11", "ngày 12", "ngày 30/11"]
DATES_TEST = ["hôm nay", "ngày mai", "ngày 18", "ngày 22/11", "ngày kia", "ngày 28 tháng 11"]

# {d} = ngày (gắn nhãn date), {i} = món, {p} = giá (gắn nhãn amount). Khung train và test KHÔNG trùng cấu trúc.
TRAIN_FRAMES = [
    "{d} mua {i} {p} được không", "{d} mua {i} giá {p} có ổn không", "mua {i} {p} {d} được không", "{d} mình định mua {i} {p} có ổn không",
    "tui muốn mua {i} {p} {d} thì có đủ tiền không", "{d} sắm {i} {p} được không", "{d} mua {i} {p} có sao không nhỉ", "đợi tới {d} mua {i} {p} có ổn không",
    "{p} mua {i} {d} có đủ không", "thử xem {d} mua {i} {p} có ổn không", "{d} tậu {i} {p} có hợp lý không", "chốt đơn {i} {p} {d} được chứ",
]
TEST_FRAMES = [
    "cho mình hỏi {d} mua {i} {p} có được không nhé", "mua {i} hết {p}, {d} đủ tiền không", "{d} order {i} {p} ổn không", "mình đang tính rinh {i} {p} {d}, được ko",
    "{i} giá {p}, {d} mua có sao ko", "nếu {d} mua {i} {p} thì có an toàn không", "{d} mua {i} {p} đủ không bạn",
]

# Đối nghịch loại A: khoản chi bình thường có động từ mua + giá (add_expense), KHÔNG hỏi khả năng chi.
EXPENSE_TRAIN = ["mua {i} {p}", "hôm nay mua {i} {p}", "vừa mua {i} {p}", "mới sắm {i} {p}", "đã mua {i} {p} hôm qua", "mua {i} {p} ở shopee", "hôm qua sắm {i} {p}",
                 "chi {p} mua {i}", "tối qua tậu {i} {p}", "mua {i} hết {p}"]
EXPENSE_TEST = ["lúc nãy mua {i} {p}", "mình mới rinh {i} {p}", "mua xong {i} rồi hết {p}", "đặt mua {i} {p} trên lazada", "sáng nay mua {i} {p}"]

# Đối nghịch loại B: xin lời khuyên / hỏi giá trị / giá thị trường (out_of_scope). Không câu nào có chữ "nên" kèm giá (luật coi đó là hỏi khoản mua).
ADVICE_TRAIN = ["nên mua nhà hay thuê nhà", "có nên mua vàng lúc này không", "có nên mua cổ phiếu nào không", "nên mua laptop nào cho sinh viên", "nên mua xe số hay xe ga",
                "mua trả góp hay mua một lần thì tốt hơn", "nên mua bảo hiểm nào", "iphone 20 triệu có đáng mua không", "laptop 15 triệu có đáng mua không",
                "tai nghe 2 triệu có đáng mua không", "mua bitcoin lúc này có lời không", "nên mua đô la hay vàng", "có nên mua căn hộ chung cư không",
                "mua xe điện có tiết kiệm hơn xe xăng không", "5 triệu mua được laptop nào", "ngân sách 10 triệu nên chọn điện thoại gì", "giá iphone mới nhất là bao nhiêu",
                "so sánh macbook và laptop dell giúp mình", "nên mua máy giặt hãng nào", "có nên vay mua nhà không", "đồng yên nhật có đáng mua không",
                "mua tivi loại nào tốt nhất", "nên mua sách gì để học tiếng anh", "mua nhà ở khu nào tốt"]
ADVICE_TEST = ["có nên mua xe máy cũ không", "nên mua điện thoại nào", "ipad 12 triệu có đáng mua không", "mua vé máy bay giá rẻ ở đâu", "so sánh giá tủ lạnh samsung và lg",
               "mua trả góp có lợi không", "nên mua đất hay gửi tiết kiệm", "loa bluetooth nào đáng mua nhất", "có nên mua ethereum lúc này không", "giá vàng hôm nay bao nhiêu"]

# Đối nghịch loại C: hỏi về chi tiêu ĐÃ CÓ (ask_spending), có chữ mua/sắm.
ASK_TRAIN = ["hôm qua tôi đã mua gì", "tháng này tôi mua sắm hết bao nhiêu", "tuần này tôi chi bao nhiêu cho việc mua đồ", "tôi đã mua những gì hôm nay",
             "tổng tiền mua sắm tháng trước", "tháng này tôi sắm đồ hết bao nhiêu"]
ASK_TEST = ["hôm kia tôi mua gì nhỉ", "chi tiêu mua sắm tuần trước là bao nhiêu", "xem lại các khoản mua trong tháng này"]


def fold(text: str) -> str:
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def fill(frame: str, rng: random.Random, dates: list[str]) -> str:
    text = frame.replace("{d}", f"[{rng.choice(dates)}](date)").replace("{i}", rng.choice(ITEMS)).replace("{p}", f"[{rng.choice(PRICES)}](amount)")
    return re.sub(r"\s+", " ", text).strip()


def plain(text: str) -> str:
    return re.sub(r"\[([^\]]+)\]\([a-z_]+\)", r"\1", text)


def maybe_fold(text: str, rng: random.Random) -> str:
    return fold(text) if rng.random() < UNACCENTED_SHARE else text


def sample(frames: list[str], dates: list[str], count: int, rng: random.Random, seen: set[str]) -> list[str]:
    out: list[str] = []
    attempts = 0
    while len(out) < count and attempts < count * 60:
        attempts += 1
        text = maybe_fold(fill(rng.choice(frames), rng, dates), rng)
        key = plain(text).lower()
        if key not in seen:
            seen.add(key)
            out.append(text)
    return out


def row(text: str, intent: str, split: str, tag: str) -> dict:
    return {"text": text, "intent": intent, "tag": tag, "split": split}


def check(rows: list[dict]) -> None:
    """Nhãn phải khớp luật: câu evaluate_purchase ra bản nháp; câu đối nghịch không ra gì."""
    bad = []
    for r in rows:
        result = parse_purchase(plain(r["text"]), NOW)
        if r["intent"] == "evaluate_purchase":
            if result is None or result.draft is None or result.needs_amount:
                bad.append(("không ra bản nháp", r["text"]))
        elif result is not None:
            bad.append(("lẽ ra không phải khoản định mua", r["text"]))
    if bad:
        raise SystemExit("Nhãn lệch luật:\n" + "\n".join(f"  [{why}] {text}" for why, text in bad[:40]) + f"\n(tổng {len(bad)})")


def main() -> None:
    rng = random.Random(SEED)
    held_out = {plain(json.loads(line)["text"]).lower() for line in FIXTURE.read_text(encoding="utf-8").splitlines() if line.strip().startswith("{")}
    seen: set[str] = set(held_out)
    rows: list[dict] = []

    for text in sample(TRAIN_FRAMES, DATES_TRAIN, 170, rng, seen):
        rows.append(row(text, "evaluate_purchase", "train", "purchase"))
    for text in sample(TEST_FRAMES, DATES_TEST, 50, rng, seen):
        rows.append(row(text, "evaluate_purchase", "test", "purchase_eval"))
    for text in sample(EXPENSE_TRAIN, DATES_TRAIN, 70, rng, seen):
        rows.append(row(text, "add_expense", "train", "purchase"))
    for text in sample(EXPENSE_TEST, DATES_TEST, 20, rng, seen):
        rows.append(row(text, "add_expense", "test", "purchase_eval"))
    for split, tag, lines in (("train", "purchase", ADVICE_TRAIN), ("test", "purchase_eval", ADVICE_TEST)):
        rows += [row(maybe_fold(t, rng), "out_of_scope", split, tag) for t in lines if plain(t).lower() not in held_out]
    for split, tag, lines in (("train", "purchase", ASK_TRAIN), ("test", "purchase_eval", ASK_TEST)):
        rows += [row(maybe_fold(t, rng), "ask_spending", split, tag) for t in lines if plain(t).lower() not in held_out]

    check(rows)
    OUT.parent.mkdir(exist_ok=True)
    OUT.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")
    counts: dict[tuple[str, str], int] = {}
    for r in rows:
        counts[(r["split"], r["intent"])] = counts.get((r["split"], r["intent"]), 0) + 1
    print(f"Đã ghi {len(rows)} câu vào {OUT.relative_to(ROOT)}")
    for (split, intent), n in sorted(counts.items()):
        print(f"  {split:5} {intent:18} {n}")


if __name__ == "__main__":
    main()
