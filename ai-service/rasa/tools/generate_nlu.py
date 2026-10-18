#!/usr/bin/env python3
"""Sinh dữ liệu NLU tiếng Việt cho Rasa theo mẫu có kiểm soát (dữ liệu TỔNG HỢP, không phải dữ liệu người dùng thật).

Ra hai tệp:
  data/nlu_generated.yml      dùng để HUẤN LUYỆN
  tests/nlu_test_v2.yml       dùng để ĐO, tách riêng

Chống rò rỉ: bộ test dùng mẫu câu, món, cửa hàng, ngày, kiểu hỏi KHÁC HẲN bộ train (kiểm tra ở cuối; vi phạm thì dừng). Mỗi câu có bản có dấu
và (một phần) bản không dấu. Số tiền viết bằng nhiều kiểu: 20k, 2tr5, 3 củ 8, "hai mươi nghìn", 50.000đ... Seed cố định nên chạy lại ra đúng tệp cũ.

Chạy (từ ai-service):  PYTHONPATH=. python rasa/tools/generate_nlu.py
"""
from __future__ import annotations

import pathlib
import random
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))
from app.services.vi_text import fold  # noqa: E402

RASA = pathlib.Path(__file__).resolve().parents[1]
SEED = 20261012

Seg = tuple[str, str | None]  # (chữ, thực thể hoặc None)

# ---------------------------------------------------------------- từ vựng: TRAIN và TEST không giao nhau
AMOUNTS_TRAIN = ["20k", "35k", "45k", "60k", "120k", "250k", "15 nghìn", "45 nghìn", "180 nghìn", "2tr5", "1tr2", "1 triệu", "3 triệu", "2 triệu rưỡi",
                 "3 củ 8", "14 củ", "50.000đ", "85.000", "120.000đ", "50 cành", "25 cành", "1 lít 2", "hai mươi nghìn", "năm mươi nghìn",
                 "một trăm nghìn", "bốn mươi lăm nghìn", "một triệu hai", "ba trăm nghìn"]
AMOUNTS_TEST = ["30k", "55k", "75k", "95k", "140k", "320k", "25 nghìn", "70 nghìn", "210 nghìn", "3tr5", "1tr8", "2 triệu", "5 triệu",
                "1 triệu rưỡi", "4 củ 2", "7 củ", "65.000đ", "95.000", "200.000đ", "80 cành", "35 cành", "2 lít 5", "ba mươi nghìn", "sáu mươi nghìn",
                "hai trăm nghìn", "hai mươi lăm nghìn", "một triệu rưỡi", "năm trăm nghìn"]

ITEMS_TRAIN = ["ăn cơm", "ăn phở", "cà phê", "uống trà sữa", "đổ xăng", "đi grab", "gửi xe", "mua thuốc", "đi chợ", "tiền điện", "tiền nước", "tiền nhà",
               "đóng học phí", "xem phim", "mua áo", "mua giày", "nạp tiền điện thoại", "đóng tiền mạng", "cắt tóc", "mua sữa bột", "mua tã",
               "mua bột giặt", "đi taxi", "ăn lẩu", "uống bia", "mua rau", "mua sách", "khám bệnh", "mua vitamin", "đi siêu thị", "ăn sáng", "ăn trưa",
               # Mở rộng 2026-10-18: model nhớ "món -> ý định" nên gặp món lạ thì đoán theo chữ lạ ("nạp thẻ game" -> chuyển tiền). Thêm món đủ nhóm và
               # nhiều động từ (trả tiền / nạp / thuê / đóng) cùng nằm trong chi tiêu để nó học động từ không quyết định ý định. Không trùng ITEMS_TEST.
               "trả tiền điện", "trả tiền wifi", "trả tiền nước", "trả tiền thuê nhà", "trả tiền học", "trả tiền điện thoại", "nạp thẻ điện thoại",
               "nạp tiền game", "nạp card", "nạp thẻ garena", "nạp xăng", "thuê xe", "thuê nhà", "thuê sân bóng", "thuê áo dài", "đóng tiền điện", "đóng tiền nước",
               "đóng tiền học thêm", "đóng quỹ lớp", "đóng tiền ký túc xá", "ăn bún", "ăn mì", "ăn bánh xèo", "ăn cơm hộp", "ăn vặt", "uống nước ngọt", "uống sinh tố",
               "uống cà phê", "mua nước", "mua kem", "mua trứng", "mua thịt", "mua gạo", "mua dầu ăn", "mua sữa tươi", "đổ dầu", "rửa xe", "sửa xe", "thay nhớt",
               "mua vé xe", "mua vé tàu", "mua điện thoại", "mua tai nghe", "mua sạc", "mua quần", "mua váy", "mua mũ", "mua balo", "mua son", "mua kem chống nắng",
               "mua hoa", "mua quà sinh nhật", "mừng cưới", "đi karaoke", "đi spa", "đi du lịch", "làm móng", "uốn tóc", "mua cát mèo", "mua hạt cho mèo", "khám mắt",
               "cắt kính", "mua thuốc cảm", "tiêm phòng", "mua sách giáo khoa", "photo tài liệu", "in tài liệu",
               # đợt 3 (2026-10-18): danh từ/khoản định kỳ đứng một mình ("youtube premium 79k") và động từ đa dạng, để model nhìn khung câu thay vì nhớ món
               "youtube premium", "icloud", "chatgpt", "canva", "google one", "thẻ yoga", "vé tháng xe buýt", "học phí tiếng nhật", "tiền gửi hành lý",
               "phí gửi hàng", "tiền nước uống", "tiền rác", "tiền vệ sinh", "tiền bảo vệ", "tiền xe", "tiền ăn", "tiền chợ", "tiền học", "tiền trọ",
               "tiền sửa nhà", "tiền sơn nhà", "tiền thợ điện", "tiền thợ nước", "tiền giữ xe", "tiền phạt giao thông", "tiền cược", "tiền tip", "tiền thuốc",
               "sơn tường", "lắp điều hòa", "bảo dưỡng xe", "đăng kiểm xe", "thay pin điện thoại", "mua cục sạc", "mua chuột máy tính", "mua sách tiếng anh",
               "mua hộp bút", "mua cặp sách", "mua bàn học", "mua đệm", "mua rèm cửa", "mua nồi chiên", "mua ấm đun nước", "mua bàn chải", "mua khăn tắm",
               "uống ly matcha", "uống cacao", "ăn buffet", "ăn lẩu nướng", "ăn bún đậu", "ăn xôi", "ăn hủ tiếu", "ăn cháo", "mua bánh tráng", "gọi đồ ăn vặt",
               "đặt cơm văn phòng", "đặt bánh kem", "đặt hoa", "ủng hộ từ thiện", "mừng sinh nhật", "mừng đầy tháng", "đi chùa", "đi biển", "đi xem bóng đá",
               "đi chơi game", "đi làm tóc", "đi nhổ răng", "đi tiêm", "đi xét nghiệm", "đi khám da", "đi siêu âm"]
ITEMS_TEST = ["ăn bún bò", "uống nước mía", "đổ bình xăng", "đi xe buýt", "mua cá", "mua bánh mì", "trả tiền gas", "đóng tiền rác", "mua quần jean",
              "mua khẩu trang", "khám nha khoa", "mua đồ chơi cho bé", "nạp thẻ game", "xem concert", "giặt ủi", "mua dầu gội", "thuê phòng trọ",
              "đóng bảo hiểm", "ăn chè", "mua trái cây", "gọi đồ ăn", "mua nồi cơm điện", "đi cà phê", "ăn tối"]

MERCHANTS_TRAIN = ["Highlands", "Phúc Long", "Circle K", "Ministop", "Winmart", "Bách Hóa Xanh", "Grab", "Petrolimex", "Cơm Tấm Cali", "KFC", "Shopee"]
MERCHANTS_TEST = ["GS25", "Lotte Mart", "Coopmart", "Gojek", "Be", "Phở 24", "Guardian", "Long Châu", "Pizza 4P's", "Tiki"]

DATES_TRAIN = ["hôm qua", "hôm nay", "sáng nay", "chiều nay", "tối qua", "hôm kia", "ngày 5/10", "ngày 12/10", "15/10"]
DATES_TEST = ["trưa nay", "tối nay", "ngày 3/10", "ngày 20 tháng 9", "sáng qua", "hôm thứ ba", "22/9"]

INCOME_TRAIN = ["nhận lương", "lương tháng này", "được thưởng", "bán đồ cũ", "nhận tiền freelance", "mẹ cho", "được lì xì", "hoàn tiền", "nhận tiền làm thêm",
                "mẹ gửi", "chị gửi", "anh gửi", "bố cho", "chú cho", "nhận tiền bán hàng", "nhận tiền thưởng", "nhận tiền lãi", "nhận học bổng",
                "khách chuyển khoản", "bạn trả lại tiền", "được trả nợ", "bán điện thoại cũ", "dì út gửi", "bác cả gửi", "cô út gửi", "chị hai gửi", "sếp thưởng",
                "nhận tiền cổ tức", "nhận tiền bồi thường", "khách boa", "nhận tiền trợ cấp", "bán đồ cũ trên chợ", "tiền lãi gửi kỳ hạn"]
INCOME_TEST = ["lương về", "khách trả tiền", "bán xe cũ", "được tặng", "nhận tiền dạy thêm", "ba gửi", "tiền thưởng tết", "cashback"]

TRANSFER_TRAIN = ["chuyển {a} sang tiết kiệm", "rút {a} tiền mặt", "nạp {a} vào momo", "chuyển khoản {a} cho mẹ", "trả nợ bạn {a}", "chuyển {a} từ ví sang thẻ",
                  "gửi tiết kiệm {a}", "đảo nợ thẻ {a}",
                  "nạp {a} vào ví apple", "nạp {a} vào ví lazada", "nạp {a} vào ví viettel", "nạp {a} vào ví phụ", "nạp {a} vào tài khoản ngân hàng",
                  "rút {a} về ví", "chuyển {a} qua thẻ atm", "chuyển {a} về tài khoản phụ", "gửi tiết kiệm kỳ hạn {a}", "gởi tiết kiệm {a}",
                  "rút {a} từ tiết kiệm", "chuyển {a} từ thẻ sang ví", "gửi bố {a}", "chuyển cho anh {a}", "gửi cho chị {a}",
                  "rút tiền {a}", "rút tiền mặt {a}", "đi rút tiền {a}", "vừa rút tiền {a}", "rút {a} tiền"]
TRANSFER_TEST = ["chuyển {a} qua tài khoản ngân hàng", "rút {a} ở atm", "nạp {a} vào zalopay", "chuyển tiền cho em {a}", "trả tiền nợ anh {a}",
                 "bỏ {a} vào tiết kiệm", "chuyển {a} sang ví momo"]

PERIODS_TRAIN = ["tháng này", "hôm nay", "hôm qua", "tuần này", "tháng trước"]
PERIODS_TEST = ["tuần trước", "năm nay", "3 ngày qua", "sáng nay"]
CATS_TRAIN = ["", "ăn uống", "đi lại", "mua sắm", "tiền nhà"]
CATS_TEST = ["", "giải trí", "sức khỏe", "cà phê"]
ASK_TRAIN = ["{p} xài bao nhiêu rồi", "cho xem chi tiêu {p}", "{p} chi cho {c} nhiêu", "{p} thu nhập được bao nhiêu", "{p} còn dư bao nhiêu",
             "{p} tôi tiêu bao nhiêu", "{p} chi bao nhiêu", "tổng chi {p}", "{p} đã tiêu {c} hết bao nhiêu", "{p} tiêu cho {c} bao nhiêu",
             "xem chi tiêu {p}", "{p} còn lại bao nhiêu", "{p} thu chi thế nào"]
ASK_TEST = ["cho mình biết {p} đã chi bao nhiêu", "thống kê {p}", "{p} mình xài hết bao nhiêu tiền", "chi tiêu {c} {p} là bao nhiêu",
            "tính giúp tổng tiền {p}", "{p} dư bao nhiêu"]

OOS_TRAIN = ["đổi {a} sang yên nhật", "{a} có đủ mua nhà không", "gửi {a} ngân hàng nào lãi cao", "tỷ giá euro hôm nay", "bao nhiêu đô la đổi được {a}",
             "với {a} một tháng sống ở sài gòn được không", "{a} mua xe máy gì được", "tiền {a} đầu tư gì tốt", "thời tiết hôm nay thế nào", "kể chuyện cười đi", "giá vàng hôm nay bao nhiêu", "bạn tên gì", "iphone {a} có đáng mua không",
             "mua nhà trả góp hay trả thẳng", "tư vấn đầu tư cổ phiếu giúp mình", "dịch giúp mình câu này sang tiếng anh", "bóng đá tối nay mấy giờ",
             "xe {a} có nên mua không", "làm sao để giàu nhanh", "hát một bài đi"]
OOS_TEST = ["hôm nay trời đẹp quá", "ai là tổng thống nước mỹ", "laptop {a} có tốt không", "mua bitcoin bây giờ được không", "nấu phở thế nào ngon",
            "có nên vay tiền mua xe", "bạn bao nhiêu tuổi", "đặt vé máy bay giúp mình", "lãi suất ngân hàng hiện nay"]

# ---------------------------------------------------------------- mẫu câu: TRAIN và TEST khác nhau về cấu trúc
EXPENSE_TRAIN = ["{item} {amt}", "{item} hết {amt}", "{date} {item} {amt}", "{item} {amt} {date}", "vừa {item} {amt}", "mình {item} tốn {amt}",
                 "hết {amt} vì {item}", "tiêu {amt} vào {item}", "bỏ ra {amt} {item}", "{item} tới {amt}", "hôm nay mình {item} hết {amt}", "đã {item} {amt}",
                 "{item} ở {merchant} {amt}", "chi {amt} {item}",
                 "{amt} {item}", "{amt} cho {item}", "hết {amt} cho {item}", "hết {amt} tiền {item}", "trả {amt} tiền {item}", "mất {amt} vì {item}",
                 "chi {amt} cho {item}", "{item} hết khoảng {amt}", "{item} giá {amt}", "{item} mình trả {amt}", "thanh toán {item} {amt}", "{item}: {amt}",
                 "{item} tháng này {amt}", "{item} tuần này hết {amt}", "tuần này {item} {amt}", "mới {item} {amt}", "vừa mới {item} hết {amt}",
                 "{item} {amt} nhé", "{item} {amt} luôn", "nay mình {item} {amt} nha", "mình {item} hết {amt} ạ", "{item} hôm nay {amt}",
                 # cụm chi tiêu ngắn CHƯA có số tiền ("đi ăn", "mua nước"): vẫn là add_expense, ai-service hỏi lại số tiền
                 "{item}", "vừa {item}", "nay {item}", "mình {item}", "{date} {item}",
                 "đã chi {amt} {item}", "ghi giúp mình {item} {amt}", "ghi {item} {amt}", "{item} ({amt})", "{item} - {amt}", "{item} {amt}.", "{item} {amt}, {item2} {amt2}", "{item} {amt} với {item2} {amt2}",
                 "{item} {amt} và {item2} {amt2}", "{date} {item} {amt} {item2} {amt2}"]
EXPENSE_TEST = ["tốn {amt} cho {item}", "{item} mất {amt}", "{item} khoảng {amt}", "nay {item} hết {amt} rồi", "{item} {amt} rồi {item2} {amt2}",
                "{date}, {item} {amt}", "{item} xong hết {amt}", "ghé {merchant} {item} {amt}", "{item} {amt}, thêm {item2} {amt2}"]
INCOME_TRAIN_T = ["{x} {amt}", "{date} {x} {amt}", "vừa {x} {amt}", "{x} được {amt}", "hôm nay {x} {amt}", "được {x} {amt}", "{x} {amt} nhé", "{x} {amt} luôn"]
INCOME_TEST_T = ["tháng này {x} {amt}", "{amt} tiền {x}", "{x}: {amt}", "báo {x} {amt}"]


def pick(rng: random.Random, seq):
    return rng.choice(seq)


def render(parts: list[Seg], accent: bool) -> str:
    out = []
    for text, ent in parts:
        text = text if accent else fold(text)
        out.append(f"[{text}]({ent})" if ent else text)
    return " ".join("".join(out).split())


def build_expense(rng, tpl, items, amounts, merchants, dates):
    chosen = {"item": pick(rng, items), "item2": pick(rng, items), "amt": pick(rng, amounts), "amt2": pick(rng, amounts),
              "merchant": pick(rng, merchants), "date": pick(rng, dates)}
    return fill(tpl, chosen, {"amt": "amount", "amt2": "amount", "merchant": "merchant", "date": "date"})


def fill(tpl: str, values: dict[str, str], entities: dict[str, str]) -> list[Seg]:
    parts: list[Seg] = []
    i = 0
    while i < len(tpl):
        if tpl[i] == "{":
            j = tpl.index("}", i)
            key = tpl[i + 1 : j]
            parts.append((values[key], entities.get(key)))
            i = j + 1
        else:
            j = tpl.find("{", i)
            j = len(tpl) if j == -1 else j
            parts.append((tpl[i:j], None))
            i = j
    return parts


def generate(split: str, rng: random.Random) -> dict[str, list[str]]:
    train = split == "train"
    amounts = AMOUNTS_TRAIN if train else AMOUNTS_TEST
    items = ITEMS_TRAIN if train else ITEMS_TEST
    merchants = MERCHANTS_TRAIN if train else MERCHANTS_TEST
    dates = DATES_TRAIN if train else DATES_TEST
    n = {"add_expense": 900, "add_income": 220, "transfer_money": 200, "ask_spending": 180, "out_of_scope": 150} if train else \
        {"add_expense": 170, "add_income": 40, "transfer_money": 35, "ask_spending": 40, "out_of_scope": 40}
    out: dict[str, list[str]] = {k: [] for k in n}

    def add(intent: str, parts: list[Seg]):
        accent = rng.random() > 0.35  # ~35% không dấu
        out[intent].append(render(parts, accent))

    for _ in range(n["add_expense"] * 3):
        if len(set(out["add_expense"])) >= n["add_expense"]:
            break
        add("add_expense", build_expense(rng, pick(rng, EXPENSE_TRAIN if train else EXPENSE_TEST), items, amounts, merchants, dates))
    for _ in range(n["add_income"] * 4):
        if len(set(out["add_income"])) >= n["add_income"]:
            break
        values = {"x": pick(rng, INCOME_TRAIN if train else INCOME_TEST), "amt": pick(rng, amounts), "date": pick(rng, dates)}
        add("add_income", fill(pick(rng, INCOME_TRAIN_T if train else INCOME_TEST_T), values, {"amt": "amount", "date": "date"}))
    for _ in range(n["transfer_money"] * 4):
        if len(set(out["transfer_money"])) >= n["transfer_money"]:
            break
        add("transfer_money", fill(pick(rng, TRANSFER_TRAIN if train else TRANSFER_TEST), {"a": pick(rng, amounts)}, {"a": "amount"}))
    for _ in range(n["ask_spending"] * 4):
        if len(set(out["ask_spending"])) >= n["ask_spending"]:
            break
        tpl = pick(rng, ASK_TRAIN if train else ASK_TEST)
        cat = pick(rng, CATS_TRAIN if train else CATS_TEST)
        if "{c}" in tpl and not cat:
            continue
        parts = fill(tpl.replace("{p}", pick(rng, PERIODS_TRAIN if train else PERIODS_TEST)).replace("{c}", cat), {}, {})
        add("ask_spending", parts)
    for _ in range(n["out_of_scope"] * 4):
        if len(set(out["out_of_scope"])) >= n["out_of_scope"]:
            break
        add("out_of_scope", fill(pick(rng, OOS_TRAIN if train else OOS_TEST), {"a": pick(rng, amounts)}, {}))
    return {k: sorted(set(v)) for k, v in out.items()}


def write_yaml(path: pathlib.Path, data: dict[str, list[str]], header: str) -> None:
    lines = ['version: "3.1"', "", header, "nlu:"]
    for intent, examples in data.items():
        lines += [f"- intent: {intent}", "  examples: |"] + [f"    - {e}" for e in examples] + [""]
    path.write_text("\n".join(lines), encoding="utf-8")


def strip_markup(example: str) -> str:
    import re

    return re.sub(r"\[([^\]]+)\]\([a-z_]+\)", r"\1", example)


def main() -> int:
    rng = random.Random(SEED)
    train = generate("train", rng)
    test = generate("test", random.Random(SEED + 1))  # dòng ngẫu nhiên riêng: sửa bộ học không làm đổi bộ đo

    # --- chống rò rỉ: không câu nào trùng, và từ vựng/mẫu hai phía không giao nhau
    train_texts = {strip_markup(e) for v in train.values() for e in v}
    test_texts = {strip_markup(e) for v in test.values() for e in v}
    overlap = train_texts & test_texts
    if overlap:
        print(f"RÒ RỈ: {len(overlap)} câu trùng giữa train và test, ví dụ {sorted(overlap)[:3]}")
        return 1
    for name in ("AMOUNTS", "ITEMS", "MERCHANTS", "DATES", "INCOME", "TRANSFER", "PERIODS", "CATS", "ASK", "OOS"):
        both = set(globals()[f"{name}_TRAIN"]) & set(globals()[f"{name}_TEST"])
        if name == "CATS":
            both.discard("")
        if both:
            print(f"RÒ RỈ từ vựng {name}: {sorted(both)}")
            return 1
    handwritten = {strip_markup(l.strip()[2:]).strip() for f in sorted((RASA / "tests").glob("nlu_test*.yml")) if f.name != "nlu_test_v2.yml"
                   for l in f.read_text(encoding="utf-8").splitlines() if l.startswith("    - ")}
    copied = train_texts & handwritten
    if copied:
        print(f"RÒ RỈ: {len(copied)} câu của bộ thử viết tay nằm trong bộ học, ví dụ {sorted(copied)[:3]}")
        return 1
    if set(EXPENSE_TRAIN) & set(EXPENSE_TEST) or set(INCOME_TRAIN_T) & set(INCOME_TEST_T):
        print("RÒ RỈ mẫu câu giữa train và test")
        return 1

    write_yaml(RASA / "data" / "nlu_generated.yml", train,
               "# TỔNG HỢP: sinh bởi rasa/tools/generate_nlu.py (seed cố định). KHÔNG sửa tay; sửa mẫu trong script rồi chạy lại.\n# Không phải dữ liệu người dùng thật.")
    write_yaml(RASA / "tests" / "nlu_test_v2.yml", test,
               "# TỔNG HỢP, GIỮ RIÊNG để đo: mẫu câu/món/cửa hàng khác hẳn bộ huấn luyện. Sinh bởi rasa/tools/generate_nlu.py.")
    print({k: len(v) for k, v in train.items()}, "train =", sum(len(v) for v in train.values()))
    print({k: len(v) for k, v in test.items()}, "test =", sum(len(v) for v in test.values()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
