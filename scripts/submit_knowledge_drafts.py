"""Kiểm tra và gửi tài liệu tri thức NHÁP (Draft) lên API admin của FinMate. Không bao giờ duyệt.

    python scripts/submit_knowledge_drafts.py docs/knowledge-drafts/group-a-finmate.json            # chỉ kiểm tra (mặc định)
    FINMATE_ADMIN_TOKEN=... python scripts/submit_knowledge_drafts.py FILE --apply --base-url http://localhost:8080

File là một mảng JSON: mỗi phần tử {title, source_name, source_url, as_of, effective_from, effective_to, text}, đúng body của
POST /api/v1/admin/knowledge/documents. Token đăng nhập admin lấy từ biến môi trường FINMATE_ADMIN_TOKEN (không nhận qua dòng lệnh).
Tài liệu trùng tiêu đề (mọi trạng thái) bị bỏ qua nên chạy lại không tạo bản trùng.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

LIMITS = {"title": 200, "source_name": 200, "source_url": 500, "text": 50_000}
REQUIRED = ("title", "source_name", "as_of", "effective_from", "text")
ALLOWED = {"title", "source_name", "source_url", "as_of", "effective_from", "effective_to", "text"}

# Bộ kiểm chứng của agent chỉ cho trích số tiền trùng giá trị một bằng chứng; đoạn tài liệu là chữ nên không có giá trị tiền.
# Số tiền cụ thể trong tài liệu sẽ bị từ chối khi agent trích lại, nên cảnh báo ở đây.
_DATE = re.compile(r"\b(?:\d{1,2}/)?\d{1,2}/\d{4}\b")
_YEAR = re.compile(r"(?i)\bnăm\s+(?:19|20)\d{2}\b")
_MONEY = re.compile(
    r"(?<![\d.,])(?:\d{1,3}(?:[.,]\d{3})+|\d{4,})(?![\d])"  # 1.500.000 hoặc 1500000
    r"|(?<![\d.,])\d+(?:[.,]\d+)?\s*(?:triệu|tr|nghìn|ngàn|tỷ|k|đồng|vnđ|vnd|đ)(?!\w)",
    re.IGNORECASE,
)


def money_like(text: str) -> list[str]:
    return [m.group(0).strip() for m in _MONEY.finditer(_DATE.sub(" ", _YEAR.sub(" ", text)))]


def validate(doc: object, now: datetime) -> tuple[list[str], list[str]]:
    """(lỗi chặn gửi, cảnh báo)."""
    if not isinstance(doc, dict):
        return ["phần tử không phải một object"], []
    errors = [f"thiếu trường {k}" for k in REQUIRED if not doc.get(k)]
    errors += [f"trường lạ {k}" for k in doc if k not in ALLOWED]
    for key, limit in LIMITS.items():
        value = doc.get(key)
        if isinstance(value, str) and len(value) > limit:
            errors.append(f"{key} dài {len(value)} > {limit}")
    when: dict[str, datetime] = {}
    for key in ("as_of", "effective_from", "effective_to"):
        if doc.get(key):
            try:
                parsed = datetime.fromisoformat(str(doc[key]))
                when[key] = parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
            except ValueError:
                errors.append(f"{key} không phải ngày giờ ISO")
    warnings = []
    if "effective_from" in when and when["effective_from"] > now:
        warnings.append("effective_from ở tương lai: agent chưa tìm thấy cho tới lúc đó")
    if "effective_to" in when and "effective_from" in when and when["effective_to"] <= when["effective_from"]:
        errors.append("effective_to phải sau effective_from")
    if doc.get("source_url") in ("", None) and not str(doc.get("source_name", "")).startswith("FinMate"):
        warnings.append("tài liệu có nguồn ngoài nhưng không có source_url: nên ghi trang nguồn cụ thể")
    if found := money_like(str(doc.get("text", ""))):
        warnings.append(f"có vẻ chứa số tiền {found[:3]}: agent không trích lại được số tiền từ tài liệu, hãy viết bằng tháng/phần trăm/tỉ lệ")
    return errors, warnings


def _request(method: str, url: str, token: str, body: dict | None = None) -> dict:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as response:
        return json.loads(response.read() or b"{}")


def _selftest() -> None:
    assert money_like("Mỗi tháng để dành 20% thu nhập, 3–5 nhóm chi, 55/10/10 và nhắc lúc 22:00 năm 2026, ngày 31/10/2026.") == []
    assert money_like("Mua xe 600 triệu") == ["600 triệu"]
    assert money_like("giảm trừ 11.000.000 đồng") == ["11.000.000"]
    assert money_like("mỗi đồng thu nhập đều có mục đích") == []


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="*", help="một hoặc nhiều tệp JSON (mảng tài liệu)")
    parser.add_argument("--apply", action="store_true", help="gửi thật (mặc định chỉ kiểm tra)")
    parser.add_argument("--base-url", default="http://localhost:8080", help="địa chỉ backend")
    parser.add_argument("--selftest", action="store_true", help="chạy kiểm tra nội bộ của bộ dò số tiền rồi thoát")
    args = parser.parse_args()
    if args.selftest:
        _selftest()
        print("selftest ok")
        return 0
    if not args.files:
        parser.error("cần ít nhất một tệp JSON")

    now = datetime.now(timezone.utc)
    docs: list[tuple[str, dict]] = []
    bad = 0
    for path in args.files:
        with open(path, encoding="utf-8") as handle:
            items = json.load(handle)
        titles: set[str] = set()
        for index, doc in enumerate(items, 1):
            errors, warnings = validate(doc, now)
            label = f"{os.path.basename(path)}#{index} {doc.get('title', '?') if isinstance(doc, dict) else '?'}"
            if isinstance(doc, dict) and doc.get("title") in titles:
                errors.append("trùng tiêu đề trong cùng tệp")
            if isinstance(doc, dict):
                titles.add(doc.get("title", ""))
            for e in errors:
                print(f"LỖI     {label}: {e}")
            for w in warnings:
                print(f"CẢNH BÁO {label}: {w}")
            if errors:
                bad += 1
            else:
                docs.append((label, doc))
    print(f"\nĐã đọc {len(docs) + bad} tài liệu: {len(docs)} hợp lệ, {bad} có lỗi.")
    if not args.apply:
        print("Chế độ kiểm tra: chưa gửi gì. Thêm --apply (và FINMATE_ADMIN_TOKEN) để gửi bản nháp.")
        return 1 if bad else 0
    if bad:
        print("Sửa hết lỗi trước khi gửi.")
        return 1
    token = os.environ.get("FINMATE_ADMIN_TOKEN")
    if not token:
        print("Thiếu biến môi trường FINMATE_ADMIN_TOKEN.")
        return 2

    base = args.base_url.rstrip("/") + "/api/v1/admin/knowledge/documents"
    created = skipped = failed = 0
    for label, doc in docs:
        try:
            found = _request("GET", f"{base}?{urllib.parse.urlencode({'search': doc['title'][:100], 'limit': 100})}", token)
            items = (found.get("data") or {}).get("items", [])
            if any(item.get("title") == doc["title"] for item in items):
                skipped += 1
                print(f"BỎ QUA  {label}: đã có tài liệu cùng tiêu đề")
                continue
            result = _request("POST", base, token, doc)
            created += 1
            print(f"ĐÃ GỬI  {label}: Draft, {(result.get('data') or {}).get('chunks', '?')} đoạn")
        except urllib.error.HTTPError as error:
            failed += 1
            print(f"THẤT BẠI {label}: HTTP {error.code}")
        except (urllib.error.URLError, TimeoutError) as error:
            failed += 1
            print(f"THẤT BẠI {label}: {error}")
    print(f"\nTạo {created}, bỏ qua {skipped}, thất bại {failed}. Các bản nháp chưa được duyệt: OpsManager hoặc Admin duyệt ở trang admin.")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
