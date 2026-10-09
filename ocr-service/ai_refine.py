"""Nhờ một model AI (mặc định Gemini) phân loại danh mục và chuẩn hóa tên cửa hàng / mặt hàng bị OCR đọc lỗi.

Chỉ gửi CHỮ đã lọc: tên cửa hàng, vài dòng mặt hàng (đã qua privacy.sanitize_lines), tổng tiền. KHÔNG gửi ảnh.
Chỉ chạy khi có khóa API của nhà cung cấp và người dùng bật tính năng này cho lượt quét (xem main.py).
Danh mục trả về bị ép đúng 1 trong 8 tên bằng schema (enum) và kiểm tra lại ở phía server.
Đổi nhà cung cấp: viết thêm một hàm _call_xxx(prompt_text, schema, post) -> str (JSON) và đăng ký vào PROVIDERS;
chọn bằng biến môi trường FINMATE_AI_PROVIDER (mặc định "gemini"). Phần còn lại (prompt, kiểm tra kết quả) giữ nguyên.
"""
import json
import os
import re
import time
from difflib import SequenceMatcher

import httpx

from extract import CATEGORIES, _alnum, _norm

MODEL = os.environ.get("GEMINI_MODEL", "gemini-3.5-flash-lite")  # đo thật: ~1,2 giây/lượt, đúng cả 4 ca thử; gemini-2.5-flash đã ngừng cho tài khoản mới
_URL = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
_MAX_ITEMS, _MAX_ITEM_CHARS = 12, 60
_RETRY_WAIT = 1.5  # giây chờ trước lần thử lại khi dịch vụ báo quá tải tạm thời (503/429)
_SKIP_WORDS = ("tong", "thanh toan", "tien khach", "tien mat", "chiet khau", "vat", "thue", "cam on", "hoa don", "phieu", "ngay",
               "gio", "quay", "so luong", "don gia", "thanh tien", "khach", "tra lai", "gia tri", "ten hang", "www.", ".com", ".vn")

PROMPT = """Bạn là bộ chuẩn hóa dữ liệu hóa đơn Việt Nam. Dữ liệu dưới đây do OCR đọc nên có thể sai dấu, dính chữ, sai chữ.
Nhiệm vụ:
1. category: chọn ĐÚNG MỘT trong: dining (ăn uống, quán, cà phê, trà sữa), transport (xăng, taxi, xe, vé di chuyển), shopping (quần áo,
   giày dép, điện máy, mỹ phẩm, trang sức, đồ dùng), groceries (siêu thị, tiện lợi, thực phẩm, tạp hóa), housing (tiền thuê nhà/phòng trọ, điện, nước, gas, rác, phí chung cư), utilities (internet,
   điện thoại, nạp data, truyền hình), healthcare (thuốc, phòng khám, y tế), entertainment (phim, game, vui chơi, du lịch giải trí), other (không thuộc nhóm nào).
2. merchant: tên cửa hàng viết lại đúng chính tả tiếng Việt, đúng tên thương hiệu nếu nhận ra. Không chắc thì giữ nguyên, tuyệt đối không bịa.
3. items: viết lại tên từng mặt hàng đúng chính tả tiếng Việt, cùng thứ tự, không thêm bớt mặt hàng, bỏ số lượng/giá nếu có.
Chỉ trả JSON đúng cấu trúc yêu cầu."""


class AiUnavailable(Exception):
    """Chưa cấu hình khóa API."""


class AiFailed(Exception):
    """Gọi AI lỗi hoặc kết quả không hợp lệ."""


def pick_items(lines: list[str], merchant: str | None, limit: int = _MAX_ITEMS) -> list[str]:
    """Các dòng trông như mặt hàng (đủ chữ, không phải tổng/thuế/ngày/tên cửa hàng/số), tối đa [limit] dòng."""
    shop = _alnum(_norm(merchant or ""))
    items = []
    for line in lines:
        text = line.strip()
        norm = _norm(text)
        name_part = re.sub(r"[\d.,x×]+", " ", text)  # bỏ số lượng/giá để đếm chữ của tên hàng
        if sum(c.isalpha() for c in name_part) < 3 or len(text) > _MAX_ITEM_CHARS:
            continue
        if any(w in norm for w in _SKIP_WORDS) or (shop and _alnum(norm) == shop) or re.match(r"^[a-z]{1,3}\d{4,}", _alnum(norm)):
            continue
        if text.isupper() and len(text.split()) <= 4 and not any(c.isdigit() for c in text):
            continue  # dòng tiêu đề/tên cửa hàng IN HOA ngắn
        items.append(text)
        if len(items) >= limit:
            break
    return items


def parse_reply(text: str) -> dict:
    try:
        data = json.loads(text)
    except (TypeError, ValueError) as e:
        raise AiFailed("Gemini trả về dữ liệu không đọc được") from e
    if not isinstance(data, dict) or data.get("category") not in CATEGORIES:
        raise AiFailed("Gemini trả về danh mục ngoài danh sách cho phép")
    merchant = data.get("merchant")
    merchant = re.sub(r"\s+", " ", merchant).strip()[:80] if isinstance(merchant, str) else ""
    items = [re.sub(r"\s+", " ", i).strip()[:_MAX_ITEM_CHARS] for i in data.get("items", []) if isinstance(i, str) and i.strip()]
    return {"category": data["category"], "merchant": merchant, "items": items}


def _call_gemini(prompt_text: str, schema: dict, post) -> str:
    key = os.environ.get("GEMINI_API_KEY")
    if not key:
        raise AiUnavailable()
    body = {"contents": [{"role": "user", "parts": [{"text": prompt_text}]}],
            "generationConfig": {"temperature": 0, "responseMimeType": "application/json", "responseSchema": schema}}
    res = None
    for attempt in (1, 2):  # quá tải tạm thời thì thử lại một lần
        try:
            res = post(_URL.format(model=MODEL), headers={"x-goog-api-key": key}, json=body, timeout=20)
        except httpx.HTTPError as e:
            raise AiFailed("Không gọi được Gemini") from e
        if res.status_code not in (429, 503) or attempt == 2:
            break
        time.sleep(_RETRY_WAIT)
    if res.status_code != 200:
        raise AiFailed(f"Gemini trả lỗi {res.status_code}")
    try:
        return res.json()["candidates"][0]["content"]["parts"][0]["text"]
    except (KeyError, IndexError, TypeError, ValueError) as e:
        raise AiFailed("Phản hồi của Gemini không hợp lệ") from e


_JSON_HINT = ('Trả lời DUY NHẤT một JSON dạng {"category": <một trong ' + ", ".join(CATEGORIES)
              + '>, "merchant": <chuỗi>, "items": [<chuỗi>, ...]}, không thêm chữ nào khác.')


def _call_openai_compatible(prompt_text: str, schema: dict, post) -> str:
    """Bộ định tuyến/nhà cung cấp theo chuẩn Chat Completions của OpenAI (9router, OpenRouter, ...).

    Cần: FINMATE_AI_BASE_URL (vd. https://host/v1), FINMATE_AI_API_KEY, FINMATE_AI_MODEL. Không có schema ép enum nên
    danh mục chỉ được tin sau khi parse_reply kiểm tra lại.
    """
    base, key, model = (os.environ.get(n) for n in ("FINMATE_AI_BASE_URL", "FINMATE_AI_API_KEY", "FINMATE_AI_MODEL"))
    if not (base and key and model):
        raise AiUnavailable()
    body = {"model": model, "temperature": 0, "response_format": {"type": "json_object"},
            "messages": [{"role": "user", "content": prompt_text + "\n\n" + _JSON_HINT}]}
    try:
        res = post(base.rstrip("/") + "/chat/completions", headers={"Authorization": f"Bearer {key}"}, json=body, timeout=30)
    except httpx.HTTPError as e:
        raise AiFailed("Không gọi được dịch vụ AI") from e
    if res.status_code != 200:
        raise AiFailed(f"Dịch vụ AI trả lỗi {res.status_code}")
    try:
        return res.json()["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError, ValueError) as e:
        raise AiFailed("Phản hồi của dịch vụ AI không hợp lệ") from e


PROVIDERS = {"gemini": _call_gemini, "openai": _call_openai_compatible}
_SCHEMA = {
    "type": "OBJECT",
    "properties": {
        "category": {"type": "STRING", "enum": list(CATEGORIES)},
        "merchant": {"type": "STRING"},
        "items": {"type": "ARRAY", "items": {"type": "STRING"}},
    },
    "required": ["category", "merchant", "items"],
}


def refine(merchant: str | None, items: list[str], total: int | None, post=httpx.post) -> dict:
    """Gửi tên cửa hàng, mặt hàng, tổng tiền (chữ, không ảnh); nhận danh mục hợp lệ + tên đã chuẩn hóa."""
    provider = PROVIDERS.get(os.environ.get("FINMATE_AI_PROVIDER", "gemini"))
    if provider is None:
        raise AiUnavailable()
    payload = {"merchant": merchant, "items": items, "total_vnd": total}
    text = provider(PROMPT + "\n\nDữ liệu:\n" + json.dumps(payload, ensure_ascii=False), _SCHEMA, post)
    return parse_reply(text)


def _similar(a: str, b: str) -> float:
    return SequenceMatcher(None, _alnum(_norm(a)), _alnum(_norm(b))).ratio()


def apply_ai(result: dict, clean_lines: list[str], display, force_review: bool = False, refine_fn=None) -> None:
    """Dùng AI chỉ để gợi ý khi danh mục cục bộ chưa chắc (< 0.7). Lỗi/thiếu khóa thì giữ nguyên kết quả cục bộ.

    Quy tắc an toàn (rút ra từ đo thật trên 28 ảnh: AI bịa tên khi tên cửa hàng bị trống/hỏng và đổi sai danh mục đã chắc):
    - Không gọi khi đã có danh mục chắc từ bộ nhớ/thương hiệu/từ khóa, hoặc ảnh chụp màn hình app (đã có luật riêng).
    - Tên AI chỉ được nhận khi là bản sửa của tên đã đọc được (giống >= 0.55); tên cửa hàng trống thì không để AI tự đặt.
    - Không bao giờ hạ cờ cần kiểm tra: gợi ý của AI vẫn do người dùng xác nhận.
    Ghi thêm result["ai_status"]: skipped | ok | not_configured | error.
    """
    refine_fn = refine_fn or refine
    result["ai_status"] = "skipped"
    if result.get("screen_type") or result["category_confidence"] >= 0.7:
        return
    merchant = display(result["merchant"]) if result["merchant"] else None
    try:
        ai = refine_fn(merchant, pick_items(clean_lines, result["merchant"]), result["total_amount"])
    except AiUnavailable:
        result["ai_status"] = "not_configured"
        return
    except AiFailed:
        result["ai_status"] = "error"
        return
    result.update(category=ai["category"], category_source="ai", category_confidence=0.6, items=ai["items"], ai_status="ok")
    if ai["merchant"] and merchant and _similar(ai["merchant"], merchant) >= 0.55:
        result["ai_merchant"] = ai["merchant"]
