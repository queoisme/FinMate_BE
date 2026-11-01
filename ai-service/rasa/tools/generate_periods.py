#!/usr/bin/env python3
"""Sinh câu cho Rasa NLU: câu hỏi tổng có THÁNG/NĂM cụ thể ("tháng 9 tôi tiêu bao nhiêu", "năm 2025 tôi chi bao nhiêu") và câu ĐỐI LẬP ngoài chủ đề cũng có tháng/năm
("giá vàng tháng 9 bao nhiêu", "dân số việt nam năm 2025 là bao nhiêu").

Vì sao có: bộ học chỉ có "tháng này/tuần này/hôm qua", nên model đoán câu có số năm là out_of_scope với độ chắc 0,86-0,98 ("năm 2025 tôi tiêu bao nhiêu"). Lớp luật bù được
(transaction_parser, nguyên tắc 2b) nhưng gốc là dữ liệu. Phần đối lập quan trọng không kém: thiếu nó model học "có số năm/tháng + bao nhiêu = câu hỏi chi tiêu".

Train và test dùng KHUNG CÂU khác nhau (test là khung chưa từng thấy), nên số đo trên test là khả năng khái quát chứ không phải nhớ câu. Đầu ra là JSONL để NHẬP vào DB
(`scripts/nlu_examples.py import seed/nlu_periods.jsonl --write`); sau đó DB là nguồn sự thật, tệp này chỉ là đầu vào (xem rasa/README.md).

Chạy (từ gốc repo, chỉ dùng thư viện chuẩn):
    python ai-service/rasa/tools/generate_periods.py            -> ghi ai-service/seed/nlu_periods.jsonl
"""
from __future__ import annotations

import json
import pathlib
import unicodedata

OUT = pathlib.Path(__file__).resolve().parents[2] / "seed" / "nlu_periods.jsonl"
TAG = "periods"

# Cách nói kỳ. Train và test dùng các tháng/năm khác nhau một phần để model không nhớ "tháng 9" mà bỏ "tháng 4".
PERIODS_TRAIN = ["tháng 9", "tháng 12", "tháng 3", "tháng 07", "tháng 10 năm 2025", "tháng 5/2024", "năm 2025", "năm 2024", "năm nay", "năm ngoái", "năm trước", "cả năm nay"]
PERIODS_TEST = ["tháng 4", "tháng 11", "tháng 1 năm 2025", "tháng 8/2025", "năm 2023", "năm 2026", "năm ngoái", "năm nay"]

# Câu hỏi tổng về CHÍNH người dùng (intent ask_spending).
ASK_TRAIN = [
    "{p} tôi tiêu bao nhiêu",
    "{p} tôi đã chi bao nhiêu tiền",
    "tổng chi tiêu {p} là bao nhiêu",
    "{p} mình tiêu hết bao nhiêu",
    "cho mình xem chi tiêu {p}",
    "{p} ăn uống hết bao nhiêu",
    "tiền ăn {p} bao nhiêu",
    "thống kê chi tiêu {p}",
    "{p} tôi thu được bao nhiêu",
]
ASK_TEST = [
    "{p} tôi đã tốn bao nhiêu",
    "chi tiêu của tôi {p} là bao nhiêu",
    "cho tôi biết {p} tôi xài hết bao nhiêu",
    "{p} mình mua sắm hết bao nhiêu",
    "tính giúp tổng tiền tiêu {p}",
    "thu nhập {p} của mình bao nhiêu",
]

# Câu ngoài chủ đề CÓ tháng/năm và "bao nhiêu" (intent out_of_scope): kiến thức chung, giá thị trường, lịch: không phải chi tiêu của người dùng.
OOS_TRAIN = [
    "dân số việt nam {p} là bao nhiêu",
    "giá vàng {p} bao nhiêu",
    "{p} có bao nhiêu ngày",
    "lãi suất ngân hàng {p} bao nhiêu",
    "gdp việt nam {p} là bao nhiêu",
    "giá xăng {p} bao nhiêu một lít",
    "{p} thời tiết hà nội thế nào",
    "{p} có những sự kiện gì",
]
OOS_TEST = [
    "lương trung bình {p} ở việt nam bao nhiêu",
    "bao nhiêu người thất nghiệp {p}",
    "tỷ giá đô la {p} là bao nhiêu",
    "giá bitcoin {p} bao nhiêu",
    "{p} có bao nhiêu tuần",
]
# Kỳ dùng cho câu đối lập: cùng loại cách nói kỳ với câu hỏi chi tiêu, để model phải nhìn phần còn lại của câu.
OOS_PERIODS_TRAIN = ["tháng 9", "tháng 2", "năm 2025", "năm 2024", "năm nay", "năm ngoái"]
OOS_PERIODS_TEST = ["tháng 6", "tháng 3 năm 2025", "năm 2023", "năm nay"]


def fold(text: str) -> str:
    return unicodedata.normalize("NFD", text.replace("đ", "d").replace("Đ", "D")).encode("ascii", "ignore").decode()


def rows() -> list[dict]:
    out: list[dict] = []
    for templates, periods, intent, split in (
        (ASK_TRAIN, PERIODS_TRAIN, "ask_spending", "train"),
        (ASK_TEST, PERIODS_TEST, "ask_spending", "test"),
        (OOS_TRAIN, OOS_PERIODS_TRAIN, "out_of_scope", "train"),
        (OOS_TEST, OOS_PERIODS_TEST, "out_of_scope", "test"),
    ):
        for t_index, template in enumerate(templates):
            for p_index, period in enumerate(periods):
                text = template.format(p=period)
                out.append({"text": text, "intent": intent, "tag": TAG, "split": split})
                # người dùng hay gõ không dấu: thêm bản không dấu cho một phần câu TRAIN (test giữ nguyên có dấu)
                if split == "train" and (t_index + p_index) % 4 == 0:
                    out.append({"text": fold(text), "intent": intent, "tag": TAG, "split": split})
    seen: set[str] = set()
    unique = []
    for row in out:
        if row["text"] not in seen:
            seen.add(row["text"])
            unique.append(row)
    return unique


def main() -> None:
    data = rows()
    OUT.write_text("\n".join(json.dumps(row, ensure_ascii=False) for row in data) + "\n", encoding="utf-8")
    counts: dict[tuple[str, str], int] = {}
    for row in data:
        counts[(row["split"], row["intent"])] = counts.get((row["split"], row["intent"]), 0) + 1
    print(f"đã ghi {len(data)} câu vào {OUT}")
    for (split, intent), count in sorted(counts.items()):
        print(f"  {split:5} {intent:13} {count}")


if __name__ == "__main__":
    main()
