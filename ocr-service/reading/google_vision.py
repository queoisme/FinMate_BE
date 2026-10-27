"""OCR bằng Google Cloud Vision (DOCUMENT_TEXT_DETECTION) làm đường chính; lỗi hoặc chưa cấu hình thì trả None để bên gọi dùng OCR cục bộ.

Cấu hình bằng biến môi trường (không nằm trong code):
  GOOGLE_VISION_API_KEY           khóa API của Cloud Vision; không đặt = tắt, luôn dùng OCR cục bộ
  FINMATE_OCR_ENGINE              auto (mặc định: dùng Google khi có khóa) | local (ép OCR cục bộ, dùng khi đo so sánh)
  GOOGLE_VISION_TIMEOUT_SECONDS   thời gian chờ một lời gọi (mặc định 20)
  GOOGLE_VISION_COOLDOWN_SECONDS  sau một lần lỗi, tạm nghỉ Google chừng này giây để các lần quét sau không phải chờ lỗi lại (mặc định 60)

QUYẾT ĐỊNH CỦA CHỦ SẢN PHẨM (2026-10-26): ảnh hóa đơn RỜI hệ thống sang Google, luôn gửi, không hỏi thêm người dùng. Ảnh chưa qua bước che dữ liệu cá nhân
(tên, số điện thoại, địa chỉ có thể nằm trên ảnh). Câu chữ màn đồng ý phải nói đúng điều này. Không log khóa, ảnh hay chữ trên hóa đơn; khóa chỉ nằm trong header.
"""
import base64
import json
import logging
import os
import time
import urllib.error
import urllib.request

from classification import data_files

logger = logging.getLogger(__name__)

_URL = "https://vision.googleapis.com/v1/images:annotate"
_END_OF_LINE = {"EOL_SURE_SPACE", "LINE_BREAK"}  # loại ngắt sau ký tự cuối của từ cuối một dòng


class _Cooldown:
    """Trạng thái tạm nghỉ Google sau một lần lỗi (không phải ngưỡng): trước mốc ``until`` (time.monotonic()) không gọi Google."""
    until = 0.0


_COOLDOWN = _Cooldown()

Quad = list[tuple[float, float]]

_CELL_GAP_RATIO = data_files.threshold("reading.google_vision._CELL_GAP_RATIO")


class GoogleVisionError(Exception):
    """Phản hồi 200 nhưng Cloud Vision báo lỗi trong thân (hết hạn mức, khóa sai...). Chỉ mang mã lỗi, không mang chữ nào."""


def enabled() -> bool:
    return os.environ.get("FINMATE_OCR_ENGINE", "auto") != "local" and bool(os.environ.get("GOOGLE_VISION_API_KEY")) and time.monotonic() >= _COOLDOWN.until


def build_request(image_bytes: bytes) -> urllib.request.Request:
    body = {"requests": [{"image": {"content": base64.b64encode(image_bytes).decode("ascii")},
                          "features": [{"type": "DOCUMENT_TEXT_DETECTION"}], "imageContext": {"languageHints": ["vi"]}}]}
    return urllib.request.Request(_URL, data=json.dumps(body).encode("utf-8"), method="POST",
                                  headers={"Content-Type": "application/json", "X-Goog-Api-Key": os.environ["GOOGLE_VISION_API_KEY"]})


def _post(request: urllib.request.Request) -> dict:
    with urllib.request.urlopen(request, timeout=float(os.environ.get("GOOGLE_VISION_TIMEOUT_SECONDS", "20"))) as response:
        return json.load(response)


def _vertices(box: dict) -> Quad:
    return [(float(v.get("x", 0)), float(v.get("y", 0))) for v in box.get("vertices", [])]  # Vision bỏ trường bằng 0


def parse(response: dict) -> list[tuple[str, float, Quad]]:
    """Gom các từ thành dòng (ngắt dòng theo detectedBreak của ký tự cuối). Trả (chữ, độ tin cậy trung bình, 4 góc: trên-trái, trên-phải, dưới-phải, dưới-trái)."""
    lines: list[tuple[str, float, Quad]] = []
    for page in ((response.get("responses") or [{}])[0].get("fullTextAnnotation") or {}).get("pages", []):
        words: list[tuple[str, float, Quad]] = []
        for block in page.get("blocks", []):
            for paragraph in block.get("paragraphs", []):
                for word in paragraph.get("words", []):
                    symbols = word.get("symbols", [])
                    words.append(("".join(s.get("text", "") for s in symbols), float(word.get("confidence", 0.0)), _vertices(word.get("boundingBox", {}))))
                    last = (symbols[-1].get("property") or {}).get("detectedBreak", {}).get("type") if symbols else None
                    if last in _END_OF_LINE:
                        lines.extend(_cells(words))
                        words = []
        if words:
            lines.extend(_cells(words))
    return [line for line in lines if line[0].strip() and len(line[2]) == 4]


def _cells(words: list[tuple[str, float, Quad]]) -> list[tuple[str, float, Quad]]:
    """Một dòng Google thành các ô: cắt ở khoảng trống ngang giữa hai từ rộng hơn _CELL_GAP_RATIO lần bề rộng một ký tự.

    Google gộp cả hàng bảng (số lượng, tên món, số tiền ở ba cột) thành một dòng, còn bộ tách món (parsing/items.py) làm việc theo ô: mỗi cột một hộp,
    như PaddleOCR phát hiện. Cắt theo khoảng trống cột là cách hình học chung, không dựa vào chữ nào. Đo theo bề rộng ký tự chứ không theo chiều cao
    hộp: ảnh nghiêng hay dòng có ngoặc làm chiều cao hộp phình ra, còn bề rộng ký tự vẫn tỉ lệ với cỡ chữ."""
    cells, run = [], [words[0]]
    for prev, word in zip(words, words[1:]):
        if len(prev[2]) == 4 and len(word[2]) == 4:
            char = max((w[2][1][0] - w[2][0][0]) / max(len(w[0]), 1) for w in (prev, word))
            if word[2][0][0] - prev[2][1][0] > _CELL_GAP_RATIO * char:
                cells.append(run)
                run = []
        run.append(word)
    cells.append(run)
    return [_line(cell) for cell in cells]


def _line(words: list[tuple[str, float, Quad]]) -> tuple[str, float, Quad]:
    first, last = words[0][2], words[-1][2]
    quad = [first[0], last[1], last[2], first[3]] if len(first) == 4 and len(last) == 4 else []
    return " ".join(w[0] for w in words), sum(w[1] for w in words) / len(words), quad


def read_lines_or_none(image_bytes: bytes) -> list[tuple[str, float, Quad]] | None:
    """Các dòng Google đọc được, hoặc None khi nên dùng OCR cục bộ (tắt, đang tạm nghỉ, lỗi, hoặc Google không thấy chữ nào)."""
    if not enabled():
        return None
    try:
        response = _post(build_request(image_bytes))
        error = (response.get("responses") or [{}])[0].get("error")
        if error:
            raise GoogleVisionError(f"mã {error.get('code')}")
        return parse(response) or None
    except Exception as error:  # noqa: BLE001 - mạng, hạn mức, khóa sai hay phản hồi lạ: đều rơi về OCR cục bộ, lần quét không được thất bại
        _COOLDOWN.until = time.monotonic() + float(os.environ.get("GOOGLE_VISION_COOLDOWN_SECONDS", "60"))
        logger.warning("Google Vision lỗi (%s %s): dùng OCR cục bộ và tạm nghỉ Google", type(error).__name__, getattr(error, "code", ""))
        return None
