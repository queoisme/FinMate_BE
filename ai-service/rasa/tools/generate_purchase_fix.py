"""Sinh ``seed/nlu_purchase_fix.jsonl``: câu TRAIN sửa các lỗi HỒI QUY mà bản huấn luyện có ý định ``evaluate_purchase`` (v17) gây ra so với v11 trên tập test.

Các lỗi đó (so sánh câu sai sau luật của v11 và v17-s1, 2026-11-07) gom thành nhóm; mỗi nhóm có ví dụ MỚI viết khác chữ với câu test và với bộ độc lập
(``heldout_intent``, ``rasa_coverage.jsonl``), tuyệt đối không chép câu test vào train:

1. Yêu cầu đặt chỗ/đặt vé ("đặt vé máy bay giúp mình") bị đọc thành chi tiêu vì dữ liệu "đặt mua ..." mới thêm  -> out_of_scope.
2. Hỏi thông tin chung về lương/thưởng/lãi suất của người khác ("thưởng tết ngành ngân hàng bao nhiêu") bị đọc thành hỏi chi tiêu -> out_of_scope.
3. Thu nhập kiểu làm thêm / cashback / người thân gửi bị đọc thành chi tiêu -> add_income.
4. "đóng bảo hiểm ... 25 nghìn" -> add_expense; "N ngày qua ... bao nhiêu" không dấu -> ask_spending; chuyện thời tiết -> out_of_scope.

Chỉ thêm câu TRAIN: tập test của DB không đổi nên số đo của v11 vẫn so sánh được. Câu trùng DB/bộ độc lập bị loại; ``nlu_examples.py import`` còn báo XUNG ĐỘT/RÒ RỈ.

Chạy từ ``ai-service``:  ``PYTHONPATH=. python rasa/tools/generate_purchase_fix.py``
"""
from __future__ import annotations

import json
import pathlib
import random
import re
import unicodedata

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "seed" / "nlu_purchase_fix.jsonl"
FIXTURES = [ROOT / "tests" / "fixtures" / "eval" / "rasa_coverage.jsonl", ROOT / "tests" / "fixtures" / "eval" / "heldout_intent.jsonl"]
SEED = 20261107
UNACCENTED_SHARE = 0.4
TAG = "purchase_fix"

BOOK_VERBS = ["đặt", "book", "giúp mình đặt", "nhờ đặt giùm", "đặt giúp tôi"]
BOOK_OBJECTS = ["vé tàu về quê", "vé xe khách đi sapa", "phòng khách sạn ở đà lạt", "bàn nhà hàng", "xe grab ra sân bay", "lịch hẹn nha sĩ", "vé concert cuối tuần",
                "homestay ở hội an", "vé cáp treo", "chỗ ngồi rạp chiếu phim", "tour đà nẵng", "lịch cắt tóc"]
BOOK_TAILS = ["", "giúp mình", "giùm tôi", "tối nay", "cuối tuần này", "ngày mai", "nhé", "được không"]

INFO_SUBJECTS = ["lương trung bình của lập trình viên", "thưởng tết ngành ngân hàng năm nay", "mức lương tối thiểu vùng 1 hiện nay", "lãi suất tiết kiệm các ngân hàng",
                 "tỷ giá đô la hôm nay", "thu nhập bình quân đầu người ở việt nam", "lương hưu trung bình của người lao động", "doanh thu của vinamilk năm nay",
                 "tiền thưởng cuối năm của các công ty công nghệ", "giá xăng tuần này", "mức phạt vượt đèn đỏ", "lương giáo viên mới ra trường",
                 "chi phí sinh hoạt ở sài gòn của sinh viên", "giá vé máy bay đi đà nẵng", "lương bác sĩ mới vào nghề"]
INFO_TAILS = ["bao nhiêu", "là bao nhiêu", "hiện nay là bao nhiêu vậy", "khoảng bao nhiêu"]

INCOME = ["chạy grab thêm buổi tối được 350k", "dạy kèm cuối tuần nhận 500k", "nhận cashback 120k", "được hoàn cashback 80k vào ví", "cashback về ví 150k",
          "mẹ gửi tiền 3 triệu", "tiền bố gửi 1tr5", "anh trai gửi cho 2 củ", "chị gái chuyển cho 800k", "bác gửi tiền ăn học 2 triệu", "được cho 500k tiền ăn",
          "ship hàng thêm cuối tuần được 450k", "làm freelance xong nhận 4 triệu", "bán đồ cũ được 280k", "nhận tiền thù lao viết bài 1tr2", "phát tờ rơi hôm nay được 300k",
          "trực nhà hàng ca tối được 420k", "thu được 700k tiền cho thuê chỗ để xe", "nhận tiền công phụ quán 250k", "ba gửi cho 1 triệu rưỡi",
          "tiền ông bà cho 600k", "nhận 900k tiền gia sư tuần này", "cộng tác viên trả 1tr8", "được thưởng cuối tuần 200k", "vừa nhận tiền làm thêm 380k",
          "cashback shopee về 65k", "hoàn tiền momo 40k", "được lì xì thêm 300k", "bạn chuyển trả tiền mượn 500k", "người nhà gửi tiền sinh hoạt 2tr5"]

INSURANCE = ["đóng bảo hiểm xe máy 300k", "đóng bảo hiểm y tế 120 nghìn", "đóng tiền bảo hiểm khoảng 40 nghìn", "nộp phí bảo hiểm 450k", "đóng bảo hiểm nhân thọ 1tr2",
             "trả phí bảo hiểm ô tô 5 triệu", "đóng bảo hiểm xã hội tự nguyện 800k", "gia hạn bảo hiểm 250 nghìn"]

PERIOD_NUMS = [2, 4, 5, 6, 7, 9, 10, 12, 14, 20]
PERIOD_FRAMES = ["{n} ngày qua tôi chi bao nhiêu", "{n} ngày qua đã tiêu bao nhiêu", "{n} ngày vừa rồi hết bao nhiêu tiền", "trong {n} ngày qua mình xài bao nhiêu"]

WEATHER = ["hôm nay trời đẹp ghê", "trời mưa quá trời", "nay nắng to thế", "hôm nay nóng quá đi", "trời lạnh ghê", "ngoài trời gió to lắm", "chiều nay chắc mưa to",
           "sáng nay sương mù dày", "cuối tuần trời có nắng không"]


def fold(text: str) -> str:
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def key(text: str) -> str:
    return re.sub(r"\s+", " ", fold(text).lower()).strip()


def maybe_fold(text: str, rng: random.Random) -> str:
    return fold(text) if rng.random() < UNACCENTED_SHARE else text


def main() -> None:
    rng = random.Random(SEED)
    blocked: set[str] = set()
    for path in FIXTURES:
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip().startswith("{"):
                blocked.add(key(json.loads(line)["text"]))
    seen = set(blocked)
    groups: dict[str, list[dict]] = {}

    def add(group: str, text: str, intent: str) -> None:
        text = re.sub(r"\s+", " ", text).strip()
        if key(text) not in seen:
            seen.add(key(text))
            groups.setdefault(group, []).append({"text": maybe_fold(text, rng), "intent": intent, "tag": TAG, "split": "train"})

    for _ in range(120):  # lấy dư rồi cắt: nhiều khung bị trùng
        add("book", f"{rng.choice(BOOK_VERBS)} {rng.choice(BOOK_OBJECTS)} {rng.choice(BOOK_TAILS)}", "out_of_scope")
    for _ in range(80):
        add("info", f"{rng.choice(INFO_SUBJECTS)} {rng.choice(INFO_TAILS)}", "out_of_scope")
    for text in INCOME:
        add("income", text, "add_income")
    for text in INSURANCE:
        add("insurance", text, "add_expense")
    for _ in range(60):
        add("period", rng.choice(PERIOD_FRAMES).format(n=rng.choice(PERIOD_NUMS)), "ask_spending")
    for text in WEATHER:
        add("weather", text, "out_of_scope")

    limits = {"book": 40, "info": 35, "income": 99, "insurance": 99, "period": 20, "weather": 99}
    rows = [r for name, items in groups.items() for r in items[: limits[name]]]
    OUT.parent.mkdir(exist_ok=True)
    OUT.write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in rows) + "\n", encoding="utf-8")
    print(f"Đã ghi {len(rows)} câu train vào {OUT.relative_to(ROOT)}: " + ", ".join(f"{name}={min(len(items), limits[name])}" for name, items in groups.items()))


if __name__ == "__main__":
    main()
