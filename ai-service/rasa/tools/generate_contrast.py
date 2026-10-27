#!/usr/bin/env python3
"""Sinh dữ liệu NLU "cặp đối lập" cho Rasa: cùng từ khóa nhưng đảo chiều giao dịch, để model nhìn người gửi/nhận và khung câu thay vì nhớ món.

Rasa từng đoán sai mà độ chắc 1,00: "ba gửi cho 2 triệu" (tiền vào) thành chuyển tiền, "phí rút tiền" thành chuyển tiền, "tiền lãi tiết kiệm" thành chi tiêu,
"thẻ gym" thành thu nhập, "xóa dữ liệu" thành hủy khoản. Mỗi nhóm dưới đây dạy một cặp:
  {người} gửi/cho mình {tiền}  -> add_income     |  gửi/chuyển cho {người} {tiền}  -> transfer_money
  phí rút/chuyển/duy trì ...    -> add_expense    |  rút/chuyển {tiền}              -> transfer_money
  tiền lãi nhận về              -> add_income     |  trả lãi vay                    -> add_expense
  nạp {tiền} vào ví của mình    -> transfer_money |  nạp thẻ game / thẻ cào         -> add_expense
  xóa tài khoản / dữ liệu       -> out_of_scope

GIỮ RIÊNG để đo: người "ba" chỉ có trong tests/nlu_test*.yml. Nếu model đoán đúng "ba gửi cho ..." sau khi học "mẹ/chị/bác gửi cho ...", nó đã học
cấu trúc câu chứ không thuộc câu. Kiểm tra rò rỉ ở cuối: câu trùng bộ thử (kể cả bản không dấu) thì dừng.

Ra:  data/nlu_contrast.yml   (seed riêng, không đổi nlu_generated.yml)
Chạy (từ ai-service):  PYTHONPATH=. python rasa/tools/generate_contrast.py
"""
from __future__ import annotations

import pathlib
import random
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from generate_nlu import AMOUNTS_TRAIN, RASA, SEED, fill, render, strip_markup, write_yaml  # noqa: E402
from app.services.vi_text import fold  # noqa: E402

PERSON_GIVES_ME = ["mẹ", "chị", "anh", "bác", "dì", "cô", "chú", "ông nội", "bà ngoại", "sếp", "khách", "chị hai", "anh trai", "cậu"]  # không có "ba": để đo
PERSON_I_SEND = ["mẹ", "ông bà", "bác", "dì", "chú", "cô", "anh hai", "chị", "em gái", "cậu", "vợ"]
PURPOSES = ["đóng học", "tiêu vặt", "trả tiền nhà", "đi chợ", "mua xe", "sửa nhà", "ăn uống", "đóng tiền trọ", "mua sách", "đi du lịch"]

INCOME_FROM_PERSON = ["{P} gửi cho {a}", "{P} gửi về {a}", "{P} cho mình {a}", "{P} chuyển khoản cho mình {a}", "{P} cho {a} {why}", "{P} gửi {a} {why}",
                      "{P} gửi tiền {a}", "{P} vừa gửi {a}", "{P} cho tiền {a}", "{P} vừa cho {a} {why}"]
INTEREST_IN = ["lãi tiết kiệm tháng này", "tiền lãi ngân hàng", "tiền lãi về", "lãi gửi ngân hàng", "nhận lãi tiết kiệm", "lãi tiết kiệm về"]
INTEREST_IN_T = ["{x} {a}", "{x} được {a}", "vừa nhận {x} {a}"]
INTEREST_OUT = ["trả tiền lãi vay", "trả lãi ngân hàng", "đóng lãi thẻ tín dụng", "trả lãi vay mua nhà", "đóng tiền lãi trả góp"]

FEES = ["phí rút tiền", "phí chuyển khoản", "phí duy trì tài khoản", "phí giao dịch", "phí ngân hàng", "phí sms banking", "phí nạp tiền", "phí thẻ",
        "phí thường niên thẻ", "phí chuyển tiền liên ngân hàng", "phí rút tiền mặt"]
FEE_T = ["{x} {a}", "đóng {x} {a}", "bị trừ {x} {a}", "{x} tháng này {a}", "vừa bị thu {x} {a}"]
GYM = ["gia hạn gym", "đóng tiền phòng gym", "gói gym", "học yoga", "phòng tập gym", "đăng ký gym", "tập gym", "gói pilates", "vé bơi", "thẻ bơi", "học võ"]
GYM_T = ["{x} {a}", "{x} hết {a}", "tiền {x} {a}"]
PARKING = ["gửi xe ô tô", "gửi xe đạp điện", "tiền gửi xe tháng", "gửi xe ở bệnh viện", "gửi xe ở chung cư", "gửi xe ở sân bay"]
TOPUP_SERVICE = ["nạp kim cương free fire", "nạp thẻ liên quân", "nạp vcoin", "mua thẻ cào", "nạp quân huy", "nạp robux", "nạp thẻ viettel", "nạp sim", "nạp data 4g"]
ONE_SLOT = ["{x} {a}"]

WALLETS = ["shopeepay", "moca", "airpay", "ví điện tử", "ví trả sau", "ví cá nhân", "ví vnpay", "ví tiết kiệm", "ví phụ của mình"]
WALLET_T = ["nạp {a} vào {w}", "nạp tiền vào {w} {a}", "chuyển {a} vào {w}", "đổ {a} vào {w}"]
SEND_T = ["gửi {P} {a}", "gửi cho {P} {a}", "chuyển cho {P} {a}", "gửi tiền cho {P} {a}", "chuyển tiền cho {P} {a}", "gửi về quê cho {P} {a}"]

OOS = ["xóa tài khoản", "xóa lịch sử chat", "xóa hết dữ liệu của tôi", "reset tài khoản", "đổi mật khẩu", "đăng xuất", "đổi ngôn ngữ", "tắt thông báo",
       "xóa toàn bộ dữ liệu", "khóa tài khoản", "xóa ứng dụng", "đổi tên tài khoản", "cập nhật ứng dụng"]
OOS_T = ["{x}", "{x} giúp mình", "cho mình {x}", "mình muốn {x}", "{x} đi"]

# Xã giao (đo trên Rasa thật 2026-10-21): "ăn cơm chưa" thành chi tiêu, "đi ăn không" thành deny, "cảm ơn" thành confirm, "giá ... bao nhiêu" thành hỏi tổng chi tiêu.
# Rasa không có ý định "thanks" nên cảm ơn dạy là out_of_scope; ai-service nhận cụm cảm ơn bằng luật và trả lời đúng.
SMALLTALK_VERBS = ["ăn cơm", "ăn sáng", "ăn trưa", "ăn tối", "đi ăn", "uống cà phê", "uống trà", "đi chơi", "đi ngủ", "ngủ", "đi làm", "đi học", "xem phim",
                   "đi du lịch", "về quê", "tập gym", "ăn phở", "uống nước"]
SMALLTALK_PRE = ["", "bạn ", "Mầm ", "hôm nay bạn ", "mình ", "bạn đã "]
SMALLTALK_END = ["chưa", "không", "chưa nhỉ", "không nhỉ", "chưa vậy", "không vậy", "chưa bạn", "chưa Mầm"]
SMALLTALK_T = ["{pre}{v} {end}"]
GREET_MORE = ["chào bạn nha", "chào bạn ơi", "chào em", "chào anh", "hi bạn", "hello bạn ơi", "xin chào bạn nhé", "chào Mầm ơi", "chào buổi sáng bạn", "chào buổi tối nhé"]
THANKS_OOS = ["cảm ơn", "cảm ơn bạn", "cảm ơn nhiều", "cảm ơn Mầm", "thanks bạn", "thank you", "cảm ơn nhé", "cảm ơn bạn nhiều nha"]
PRICE_OOS = ["giá ethereum hôm nay bao nhiêu", "giá xăng hôm nay bao nhiêu", "giá usd bao nhiêu", "giá cổ phiếu vinamilk bao nhiêu",
             "giá vé máy bay đi đà nẵng bao nhiêu", "giá gạo bao nhiêu một ký", "iphone giá bao nhiêu", "giá điện thoại samsung bao nhiêu",
             "giá đô hôm nay bao nhiêu", "giá heo hôm nay bao nhiêu"]
PRICE_T = ["{x}", "{x} vậy", "cho mình hỏi {x}", "{x} nhỉ"]
NO_AMOUNT_T = ["{x}"]


def spec() -> list[tuple[str, list[str], dict[str, list[str]], int]]:
    """(ý định, mẫu câu, giá trị cho từng khóa, số câu tối đa)."""
    a = AMOUNTS_TRAIN
    return [
        ("add_income", INCOME_FROM_PERSON, {"P": PERSON_GIVES_ME, "a": a, "why": PURPOSES}, 150),
        ("add_income", INTEREST_IN_T, {"x": INTEREST_IN, "a": a}, 30),
        ("add_expense", ONE_SLOT, {"x": INTEREST_OUT, "a": a}, 25),
        ("add_expense", FEE_T, {"x": FEES, "a": a}, 70),
        ("add_expense", GYM_T, {"x": GYM, "a": a}, 45),
        ("add_expense", ONE_SLOT, {"x": PARKING, "a": a}, 20),
        ("add_expense", ONE_SLOT, {"x": TOPUP_SERVICE, "a": a}, 40),
        ("transfer_money", WALLET_T, {"w": WALLETS, "a": a}, 60),
        ("transfer_money", SEND_T, {"P": PERSON_I_SEND, "a": a}, 70),
        ("out_of_scope", OOS_T, {"x": OOS}, 45),
        ("out_of_scope", SMALLTALK_T, {"pre": SMALLTALK_PRE, "v": SMALLTALK_VERBS, "end": SMALLTALK_END}, 140),
        ("out_of_scope", NO_AMOUNT_T, {"x": THANKS_OOS}, 8),
        ("out_of_scope", PRICE_T, {"x": PRICE_OOS}, 40),
        ("greet", NO_AMOUNT_T, {"x": GREET_MORE}, 10),
    ]


def main() -> int:
    rng = random.Random(SEED + 2)  # dòng riêng: không ảnh hưởng nlu_generated.yml / nlu_test_v2.yml
    out: dict[str, set[str]] = {}
    for intent, templates, values, cap in spec():
        made: set[str] = set()
        for _ in range(cap * 20):
            if len(made) >= cap:
                break
            chosen = {k: rng.choice(v) for k, v in values.items()}
            parts = fill(rng.choice(templates), chosen, {"a": "amount"})
            made.add(render(parts, rng.random() > 0.35))  # ~35% không dấu như bộ sinh chung
        out.setdefault(intent, set()).update(made)
    data = {k: sorted(v) for k, v in out.items()}

    # chống rò rỉ với mọi bộ thử, cả bản có dấu lẫn không dấu
    tests = sorted((RASA / "tests").glob("nlu_test*.yml"))
    held: set[str] = set()
    for f in tests:
        for line in f.read_text(encoding="utf-8").splitlines():
            if line.startswith("    - "):
                text = strip_markup(line.strip()[2:]).strip()
                held.update((text, fold(text)))
    texts = {strip_markup(e) for v in data.values() for e in v}
    leaked = texts & held
    if leaked:
        print(f"RÒ RỈ: {len(leaked)} câu trùng bộ thử, ví dụ {sorted(leaked)[:5]}")
        return 1
    if any(fold(t).split()[:1] == ["ba"] and fold(t).split()[1:2] in (["gui"], ["cho"], ["chuyen"], ["vua"]) for t in texts):  # "bà ngoại" bỏ dấu là "ba ngoai": không tính
        print("RÒ RỈ: có câu 'ba gửi/cho ...' (người 'ba' dành cho bộ thử)")
        return 1

    write_yaml(RASA / "data" / "nlu_contrast.yml", data,
               "# TỔNG HỢP cặp đối lập (đảo chiều giao dịch): sinh bởi rasa/tools/generate_contrast.py (seed cố định). KHÔNG sửa tay.\n"
               "# Người \"ba\" cố ý không có ở đây: giữ riêng để đo xem model đã học cấu trúc câu hay chỉ thuộc câu.")
    print({k: len(v) for k, v in data.items()}, "tổng =", sum(len(v) for v in data.values()), "| đối chiếu", len(tests), "tệp thử")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
