import hmac
import io
import logging
import os
import re
import threading
import time
import uuid
from collections import OrderedDict
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import Depends, FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse
from PIL import Image
from pydantic import BaseModel

import ai_refine
import dates
import app_screens
import vietocr_onnx
from category_model import CategoryModel
from privacy import sanitize_lines
from extract import CONFIDENT_RECEIPT_SCORE, MIN_RECEIPT_SCORE, analyze, receipt_score
from knowledge_pg import open_knowledge
from merchant_name import format_merchant_name
import ocr
from items import extract_items, payable_total
from totals import choose_total
from ocr import NoTextError, read_boxes
from quality import image_quality
from storage_pg import current_user, open_stores

MAX_BYTES = 10 * 1024 * 1024

def warm_up() -> None:
    """Nạp trước PaddleOCR và VietOCR: lần quét đầu sau khi khởi động mất vài chục giây vì nạp model, quá thời gian chờ 30 giây của AI Service."""
    try:
        ocr._engine()
        if ocr._use_vietocr():
            vietocr_onnx._sessions()
    except Exception:  # noqa: BLE001 - nạp trước lỗi thì lần quét đầu tự nạp lại và báo lỗi thật ở đó
        logging.getLogger(__name__).exception("Không nạp trước được OCR")


@asynccontextmanager
async def lifespan(_app: FastAPI):
    threading.Thread(target=warm_up, daemon=True).start()  # không chặn khởi động: /health lên ngay, model nạp song song
    yield


app = FastAPI(title="FinMate OCR Service", lifespan=lifespan)
# File cạnh code như trước; có biến môi trường FINMATE_DATABASE_URL thì lưu vào PostgreSQL (xem storage_pg.py)
memory, dataset = open_stores(Path(__file__).parent)
unknown_scans, reload_knowledge = open_knowledge()  # có DB: nạp thương hiệu/từ khóa từ DB và ghi bill lạ vào DB
# Mô hình học chỉ có khi đã huấn luyện và qua cổng chặn (xem train_category_model.py); không có thì dùng luật.
category_model = CategoryModel.load(Path(__file__).parent / "category_model.joblib")

_MAX_LINES = 200
_MAX_LINE_CHARS = 200


class MerchantCategory(BaseModel):
    merchant: str
    category: str
    # chi tiết hóa đơn (tùy chọn) để ghi vào dataset
    ocr_merchant: str | None = None  # tên OCR tự đọc, khác tên người dùng sửa
    scan_id: str | None = None  # mã lượt quét: server tự lấy lại chữ đã lọc, app không phải gửi lại chữ OCR
    raw_lines: list[str] = []  # dự phòng khi không có/hết hạn scan_id (vẫn được lọc dữ liệu cá nhân)
    total_amount: int | None = None
    predicted_category: str | None = None
    predicted_source: str | None = None


# Chữ OCR đã lọc của các lượt quét gần đây, giữ trong RAM của server (không ghi đĩa) để lúc Lưu không phải gửi lại từ app.
# ponytail: bộ nhớ trong một tiến trình, mất khi khởi động lại; nhiều tiến trình thì cần kho chung (Redis).
_SCANS: "OrderedDict[str, tuple[float, list[str]]]" = OrderedDict()
_SCAN_TTL, _SCAN_MAX = 1800, 100


def _remember_scan(lines: list[str]) -> str:
    scan_id = uuid.uuid4().hex
    _SCANS[scan_id] = (time.time(), lines[:_MAX_LINES])
    while len(_SCANS) > _SCAN_MAX:
        _SCANS.popitem(last=False)
    return scan_id


def _recall_scan(scan_id: str | None) -> list[str] | None:
    entry = _SCANS.get(scan_id) if scan_id else None
    return entry[1] if entry and time.time() - entry[0] < _SCAN_TTL else None


def _error(status: int, message: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"error": message})


def _not_a_receipt(message: str) -> JSONResponse:
    return JSONResponse(status_code=422, content={"error": message, "code": "not_a_receipt"})


async def verify_internal_api_key(authorization: str | None = Header(None)) -> None:
    """Chỉ backend được gọi: `Authorization: Bearer {OCR_SERVICE_API_KEY}`, cùng kiểu với AI Service. Chưa đặt khóa thì từ chối hết, không mở."""
    key = os.environ.get("OCR_SERVICE_API_KEY")
    if not key:
        raise HTTPException(status_code=503, detail="OCR_SERVICE_API_KEY chưa được cấu hình")
    token = authorization.removeprefix("Bearer ").strip() if authorization and authorization.startswith("Bearer ") else ""
    if not hmac.compare_digest(token, key):
        raise HTTPException(status_code=401, detail="Khóa nội bộ không hợp lệ")


@app.get("/health")
def health():
    return {"status": "ok"}


_USER_HASH = re.compile(r"[0-9a-f]{16,64}")


async def bind_user(x_user_hash: str | None = Header(None)) -> None:
    """Backend (đã xác thực người dùng) truyền mã băm người dùng; bộ nhớ cửa hàng và dữ liệu lưu theo mã này.
    ponytail: API này chỉ nên gọi được từ backend; mở ra ngoài thì ai cũng giả được header này."""
    if x_user_hash is None:
        return  # dùng mã mặc định (FINMATE_USER_HASH), phù hợp máy dev
    if not _USER_HASH.fullmatch(x_user_hash):
        raise HTTPException(status_code=400, detail="X-User-Hash phải là chuỗi hex 16-64 ký tự")
    current_user.set(x_user_hash)


@app.post("/merchant-category", dependencies=[Depends(verify_internal_api_key), Depends(bind_user)])
def save_merchant_category(body: MerchantCategory):
    name = format_merchant_name(body.merchant) or body.merchant.strip()  # tên đẹp để lưu xuống
    try:
        memory.set(name, body.category, name=name)
        if body.ocr_merchant and body.ocr_merchant.strip():
            memory.set(body.ocr_merchant, body.category, name=name)  # lần sau OCR đọc ra tên cũ vẫn nhận ra và hiện tên đẹp
        unknown_scans.mark_labeled(name)
        unknown_scans.mark_labeled(body.ocr_merchant)
    except ValueError as e:
        return _error(400, str(e))
    dataset.append({
        "merchant": name,
        "ocr_merchant": body.ocr_merchant,  # giữ nguyên tên OCR gốc để dò lỗi
        "category": body.category,
        "label_source": "user",
        "predicted_category": body.predicted_category,
        "predicted_source": body.predicted_source,
        "total_amount": body.total_amount,
        "lines": _recall_scan(body.scan_id)  # chỉ giữ thứ cần để học, bỏ dữ liệu cá nhân
        or sanitize_lines([l[:_MAX_LINE_CHARS] for l in body.raw_lines[:_MAX_LINES]]),
    })
    return {"merchant": name, "category": body.category}


@app.post("/admin/reload-knowledge")
def reload_knowledge_endpoint(x_admin_token: str | None = Header(None)):
    """Áp dụng thương hiệu/từ khóa vừa sửa trong DB mà không khởi động lại. Cần FINMATE_ADMIN_TOKEN (chưa đặt thì tắt hẳn)."""
    token = os.environ.get("FINMATE_ADMIN_TOKEN")
    if not token or not x_admin_token or not hmac.compare_digest(token, x_admin_token):
        return _error(403, "Không có quyền")
    return {"reloaded": reload_knowledge()}


def _bill_items(boxes, total):
    """Tách món từ hộp chữ; tên món qua sanitize_lines. Lỗi bất ngờ không được làm hỏng lần quét."""
    try:
        res = extract_items(boxes, total)
        ok, out = res.ok, []
        for it in res.items:
            name = (sanitize_lines([it.name]) or [""])[0]
            if not name:  # tên bị lọc hết: bỏ món và không còn tin danh sách cộng đủ tổng
                ok = False
                continue
            out.append({"name": name, "amount": it.amount, "parent": it.parent, "is_discount": it.is_discount})
        return out, ok
    except Exception as e:  # noqa: BLE001 - chỉ log loại lỗi, không log tên/giá
        logging.getLogger(__name__).warning("Không tách được món: %s", type(e).__name__)
        return [], False


@app.post("/analyze-bill", dependencies=[Depends(verify_internal_api_key), Depends(bind_user)])
def analyze_bill(image: UploadFile = File(...), use_ai: bool = Form(False), share_data: bool = Form(False)):
    data = image.file.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        return _error(400, "Ảnh quá lớn (tối đa 10 MB)")
    try:
        Image.open(io.BytesIO(data)).verify()
    except Exception:
        return _error(400, "File tải lên không phải ảnh hợp lệ")
    try:
        quality = image_quality(data)
        boxes = read_boxes(data)  # đọc đúng một lần; lines suy từ hộp nên logic cũ giữ nguyên
        lines = [(b.text, b.conf) for b in boxes]
        texts = [t for t, _ in lines]
        score = receipt_score(texts)
        screen = app_screens.parse(texts)  # ảnh chụp màn hình app (chuyến xe, thanh toán dịch vụ, danh sách đơn)
        if score < MIN_RECEIPT_SCORE and not screen:
            return _not_a_receipt("Ảnh này không giống hóa đơn. Hãy chụp hoặc chọn ảnh hóa đơn rõ chữ.")
        # tổng chọn bằng ràng buộc số học (tạm tính + thuế - giảm giá, tiền khách đưa - tiền thối, cộng các món); chưa kiểm chứng được thì dùng cách đọc theo nhãn
        total_choice = (None, False) if screen else choose_total(boxes, fallback=payable_total(boxes))
        result = analyze(
            lines,
            lookup=lambda m: memory.get(m),
            predict=category_model.predict if category_model else None,
            find_known=memory.find_in_lines,
            forced_total=total_choice[0],
        )
        result["total_verified"] = total_choice[1]
        # tên đẹp người dùng đã chốt cho cửa hàng này > tên tự làm sạch từ chữ OCR
        display = lambda m: (memory.display_name(m) or format_merchant_name(m) or m) if m else None  # noqa: E731
        if screen:
            app_screens.apply(result, screen, lookup=memory.get, display=display)
        # app không nhận chữ OCR (có thể chứa điện thoại, tài khoản, tên khách/nhân viên, địa chỉ): chỉ nhận mã lượt quét
        clean_lines = sanitize_lines(result.pop("raw_lines"))
        if screen and screen.get("keep_lines") is not None:  # vd. nhãn kiện hàng: có tên/địa chỉ người nhận nên không lưu chữ nào
            clean_lines = sanitize_lines(screen["keep_lines"])
        result["scan_id"] = _remember_scan(clean_lines)
        found_date = dates.find_date(texts)  # bóc từ chữ gốc ngay trong service này; ra ngoài chỉ có ngày giờ, không có chữ gốc
        result["transacted_at"] = found_date.isoformat() if found_date else None
        result["lines"] = clean_lines  # chữ đã lọc dữ liệu cá nhân, cho AI Service bóc ngày và làm dữ liệu huấn luyện; không bao giờ trả chữ gốc
        for order in result.get("orders", []):
            order["lines"] = sanitize_lines(order.get("lines", []))
        result["quality"] = quality
        result["receipt_score"] = score
        result["items"], result["items_ok"] = ([], False) if screen else _bill_items(boxes, result["total_amount"])
        if not result["items_ok"] and sum(1 for i in result["items"] if i["parent"] is None and not i["is_discount"]) >= 2:
            result["needs_review"] = True  # đọc được bảng món nhưng cộng không ra tổng (kể cả thuế/phí/giảm giá): một trong hai đọc sai, nhắc người dùng kiểm tra
        weak_image = quality != "ok" or (score < CONFIDENT_RECEIPT_SCORE and not screen)  # ảnh kém, hoặc chỉ "hơi giống" hóa đơn
        if use_ai:  # người dùng chủ động bật: chỉ gửi tên cửa hàng, mặt hàng, tổng tiền (chữ đã lọc), không gửi ảnh
            ai_refine.apply_ai(result, clean_lines, display, force_review=weak_image)
        result["merchant_display"] = (
            memory.display_name(result["merchant"]) or result.get("ai_merchant") or display(result["merchant"])
        )  # tên người dùng đã chốt > tên AI chuẩn hóa > tên tự làm sạch
        if weak_image:
            result["needs_review"] = True
        if share_data and not screen and result["category_source"] not in ("memory", "brand"):  # người dùng đồng ý chia sẻ; bill lạ = chưa biết cửa hàng
            try:
                unknown_scans.record(result["merchant"], clean_lines, result["total_amount"], result["category"], result["category_source"], result["category_confidence"])
            except Exception:  # noqa: BLE001 - ghi DB lỗi không được làm hỏng kết quả quét của người dùng
                logging.getLogger(__name__).exception("Không ghi được bill lạ vào DB")
        return result
    except NoTextError:
        return _not_a_receipt("Không thấy chữ nào trong ảnh nên không phải hóa đơn.")
    except Exception as e:  # noqa: BLE001 - trả 500 gọn cho app test
        return _error(500, f"Lỗi nội bộ: {e}")
