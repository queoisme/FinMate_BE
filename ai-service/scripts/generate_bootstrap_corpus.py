#!/usr/bin/env python3
"""Sinh corpus BOOTSTRAP cho ``data/corpus/*.jsonl``.

> Đây là dữ liệu TỔNG HỢP, không phải thông báo thật. Nó tồn tại để pipeline chạy được
> end-to-end và có test xanh trước khi có mẫu thật. Model train trên corpus này sẽ đạt
> điểm rất cao vì mọi câu đều sinh từ đúng những template mà Extractor đã biết — con số
> đó KHÔNG nói lên độ chính xác ngoài đời.
>
> Khi có mẫu thật: thay thẳng file trong ``data/corpus/``, không cần chạy lại script này.

Chạy: ``python scripts/generate_bootstrap_corpus.py``
"""

from __future__ import annotations

import json
import pathlib
import random
import sys
from datetime import datetime, timedelta

# Chạy script bằng `python scripts/<tên>.py` từ thư mục ai-service/ — thêm gốc dự án
# vào sys.path để import được `app.*` mà không cần cài package hay set PYTHONPATH.
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from app.utils.text_utils import VIETNAM_TZ

SEED = 20260911
OUT_DIR = pathlib.Path("data/corpus")

MERCHANTS: dict[str, list[str]] = {
    "food": [
        "HIGHLANDS COFFEE",
        "PHUC LONG",
        "THE COFFEE HOUSE",
        "PHO THIN",
        "KFC VIET NAM",
        "LOTTERIA",
        "GRABFOOD",
        "SHOPEEFOOD",
        "COM TAM CALI",
        "TOCOTOCO",
        "BUN CHA HUONG LIEN",
        "CIRCLE K",
    ],
    "transport": [
        "GRAB",
        "BE GROUP",
        "GOJEK",
        "XANH SM",
        "VETC",
        "PVOIL",
        "PETROLIMEX",
        "VEXERE",
        "TAXI MAI LINH",
    ],
    "shopping": [
        "SHOPEE",
        "LAZADA",
        "TIKI",
        "WINMART",
        "BACH HOA XANH",
        "UNIQLO VIETNAM",
        "COOPMART",
        "GS25",
        "NGUYEN KIM",
    ],
    "education": [
        "TOPICA",
        "EDUMALL",
        "HOC PHI DH BACH KHOA",
        "COURSERA",
        "TRUNG TAM ANH NGU ILA",
    ],
    "housing": ["TIEN NHA THANG", "PHI QUAN LY CHUNG CU", "TIEN COC PHONG TRO"],
    "bills": [
        "EVN HCMC",
        "SAWACO NUOC SACH",
        "FPT TELECOM",
        "VIETTEL TELECOM",
        "VNPT",
        "VTVCAB",
    ],
    "entertainment": [
        "CGV CINEMAS",
        "LOTTE CINEMA",
        "NETFLIX",
        "SPOTIFY",
        "GALAXY CINEMA",
        "STEAM GAMES",
        "KARAOKE ICOOL",
    ],
    "health": [
        "NHA THUOC LONG CHAU",
        "PHARMACITY",
        "BENH VIEN VINMEC",
        "PHONG KHAM DA KHOA",
        "GUARDIAN",
    ],
    "family": ["CHUYEN TIEN ME", "CHUYEN TIEN EM TRAI", "QUA SINH NHAT CHI GAI"],
    "other": ["CHUYEN TIEN", "RUT TIEN ATM", "PHI DICH VU"],
}

INCOME_SOURCES = [
    "CONG TY ABC TRA LUONG THANG",
    "THUONG DU AN QUY",
    "HOAN TIEN DON HANG",
    "NGUYEN VAN A CHUYEN TIEN",
    "FREELANCE THANH TOAN",
]

NON_FINANCIAL = [
    "Ma OTP cua quy khach la {otp}. Ma co hieu luc trong 3 phut, tuyet doi khong chia se.",
    "Chuc mung nam moi! {brand} kinh chuc quy khach mot nam an khang thinh vuong.",
    "Uu dai thang nay: giam 50% phi chuyen tien quoc te khi giao dich tren app {brand}.",
    "Quy khach vua dang nhap {brand} tren thiet bi moi luc {time}. Neu khong phai ban, vui long lien he hotline.",
    "{brand} thong bao: he thong bao tri tu 23:00 den 02:00 ngay mai. Mong quy khach thong cam.",
    "Ban co 1 tin nhan moi tu {brand}. Mo app de xem chi tiet chuong trinh tich diem.",
    "Nhac no the tin dung: quy khach vui long thanh toan truoc ngay 15 de tranh phi tre han.",
    "Cap nhat ung dung {brand} phien ban moi de su dung tinh nang sinh trac hoc.",
]

PROVIDERS = {
    "mb_bank": {"package": "com.mbmobile", "brand": "MB Bank"},
    "vietcombank": {"package": "com.VCB", "brand": "Vietcombank"},
    "momo": {"package": "com.mservice.momotransfer", "brand": "MoMo"},
    "zalopay": {"package": "vn.com.vng.zalopay", "brand": "ZaloPay"},
    "vnpay": {"package": "vn.vnpay.vnpayewallet", "brand": "VNPay"},
}


def vnd(amount: int, style: str) -> str:
    if style == "comma":
        return f"{amount:,}"
    return f"{amount:,}".replace(",", ".")


def pick_amount(rng: random.Random, category: str) -> int:
    ranges = {
        "food": (20_000, 350_000),
        "transport": (15_000, 500_000),
        "shopping": (50_000, 3_000_000),
        "education": (500_000, 12_000_000),
        "housing": (2_000_000, 9_000_000),
        "bills": (80_000, 1_500_000),
        "entertainment": (50_000, 900_000),
        "health": (60_000, 4_000_000),
        "family": (200_000, 5_000_000),
        "other": (20_000, 2_000_000),
    }
    low, high = ranges[category]
    return rng.randrange(low, high, 1_000)


def body_for(
    provider: str,
    kind: str,
    merchant: str,
    amount: int,
    balance: int,
    moment: datetime,
    rng: random.Random,
) -> str:
    stamp = moment.strftime("%d/%m/%Y %H:%M")
    if provider == "mb_bank":
        account = f"0{rng.randrange(10**8, 10**9)}"
        if kind == "debit" and rng.random() < 0.6:
            nd = f"TT QR {merchant}"
        elif kind == "debit":
            nd = f"THANH TOAN {merchant}"
        else:
            nd = merchant
        sign = "-" if kind == "debit" else "+"
        return (
            f"TK {account}|GD: {sign}{vnd(amount, 'comma')}VND {stamp}"
            f"|SD: {vnd(balance, 'comma')}VND|ND: {nd}"
        )
    if provider == "vietcombank":
        account = f"00110{rng.randrange(10**6, 10**7)}"
        sign = "-" if kind == "debit" else "+"
        stamp_vcb = moment.strftime("%d-%m-%Y %H:%M:%S")
        ref = f"MBVCB.{rng.randrange(10**8, 10**9)}"
        return (
            f"So du TK VCB {account} {sign}{vnd(amount, 'comma')} VND luc {stamp_vcb}. "
            f"So du {vnd(balance, 'comma')} VND. Ref {ref}. ND {merchant}"
        )
    if provider == "momo":
        if kind == "debit":
            return (
                f"Bạn đã thanh toán {vnd(amount, 'dot')}đ cho {merchant}. "
                f"Số dư: {vnd(balance, 'dot')}đ"
            )
        return (
            f"Bạn đã nhận {vnd(amount, 'dot')}đ từ {merchant}. "
            f"Số dư: {vnd(balance, 'dot')}đ"
        )
    if provider == "zalopay":
        if kind == "debit":
            return (
                f"Thanh toán thành công {vnd(amount, 'dot')}đ tại {merchant}. "
                f"Số dư ví: {vnd(balance, 'dot')}đ"
            )
        return (
            f"Nạp tiền thành công {vnd(amount, 'dot')}đ từ {merchant}. "
            f"Số dư ví: {vnd(balance, 'dot')}đ"
        )
    return (
        f"Giao dich thanh cong. So tien: {vnd(amount, 'comma')} VND. "
        f"Noi dung: {merchant}. So du: {vnd(balance, 'comma')} VND"
    )


def title_for(provider: str, kind: str) -> str:
    return {
        "mb_bank": "MB Bank",
        "vietcombank": "Vietcombank",
        "momo": "MoMo",
        "zalopay": "ZaloPay",
        "vnpay": "VNPay",
    }[provider] + (" - Biến động số dư" if kind != "promo" else "")


def generate() -> None:
    rng = random.Random(SEED)
    base = datetime(2026, 8, 1, 8, 0, tzinfo=VIETNAM_TZ)
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    categories = [c for c in MERCHANTS if c != "other"]

    for provider, meta in PROVIDERS.items():
        rows: list[dict] = []

        # VNPay chỉ có luồng chi; ZaloPay/MoMo có cả nạp/nhận.
        debit_count = 26
        credit_count = 0 if provider == "vnpay" else 8

        for _ in range(debit_count):
            category = rng.choice(categories)
            merchant = rng.choice(MERCHANTS[category])
            amount = pick_amount(rng, category)
            balance = rng.randrange(500_000, 40_000_000, 1_000)
            moment = base + timedelta(
                days=rng.randrange(0, 40), minutes=rng.randrange(0, 1440)
            )
            rows.append(
                {
                    "package_name": meta["package"],
                    "title": title_for(provider, "debit"),
                    "body": body_for(
                        provider, "debit", merchant, amount, balance, moment, rng
                    ),
                    "received_at": moment.isoformat(),
                    "label": {
                        "is_financial": True,
                        "transaction_type": "debit",
                        "amount_cents": amount,
                        "merchant_name": merchant,
                        "category_slug": category,
                    },
                }
            )

        for _ in range(credit_count):
            source = rng.choice(INCOME_SOURCES)
            amount = rng.randrange(300_000, 25_000_000, 10_000)
            balance = rng.randrange(1_000_000, 60_000_000, 1_000)
            moment = base + timedelta(
                days=rng.randrange(0, 40), minutes=rng.randrange(0, 1440)
            )
            rows.append(
                {
                    "package_name": meta["package"],
                    "title": title_for(provider, "credit"),
                    "body": body_for(
                        provider, "credit", source, amount, balance, moment, rng
                    ),
                    "received_at": moment.isoformat(),
                    "label": {
                        "is_financial": True,
                        "transaction_type": "credit",
                        "amount_cents": amount,
                        "merchant_name": source,
                        "category_slug": "income",
                    },
                }
            )

        for template in NON_FINANCIAL:
            moment = base + timedelta(
                days=rng.randrange(0, 40), minutes=rng.randrange(0, 1440)
            )
            rows.append(
                {
                    "package_name": meta["package"],
                    "title": title_for(provider, "promo"),
                    "body": template.format(
                        otp=rng.randrange(100000, 999999),
                        brand=meta["brand"],
                        time=moment.strftime("%H:%M %d/%m"),
                    ),
                    "received_at": moment.isoformat(),
                    "label": {
                        "is_financial": False,
                        "transaction_type": None,
                        "amount_cents": None,
                        "merchant_name": None,
                        "category_slug": None,
                    },
                }
            )

        rng.shuffle(rows)
        path = OUT_DIR / f"{provider}.jsonl"
        with path.open("w", encoding="utf-8") as handle:
            for row in rows:
                handle.write(json.dumps(row, ensure_ascii=False) + "\n")
        print(f"{path}: {len(rows)} mẫu")


if __name__ == "__main__":
    generate()
