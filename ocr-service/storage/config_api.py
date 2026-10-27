"""Từ vựng và ngưỡng của lớp đọc hóa đơn lấy từ tài liệu cấu hình của AI Service (admin sửa từ trang quản trị) và gắn vào các mô-đun OCR. (Bản trước đọc thẳng bảng ``config_documents``: ``config_pg.py``.)

Nạp lúc khởi động và định kỳ (``POLL_SECONDS``): admin kích hoạt một bản thì dịch vụ đang chạy dùng nó trong vòng chừng đó thời gian, không cần build lại.
An toàn: dựng xong TOÀN BỘ giá trị mới rồi mới gán (hỏng giữa chừng thì không đổi gì); khóa nào thiếu trong bản của AI Service thì dùng mặc định trong data/; bản hỏng thì
bỏ qua và giữ bản đang chạy (ghi một lần cho mỗi phiên bản hỏng, không lặp mỗi nhịp). Không có API hoặc chưa có bản nào được kích hoạt thì dùng data/*.json như trước. Không log nội dung.

Giới hạn đã biết: gán lại thuộc tính mô-đun có hiệu lực ngay cho các hàm trong mô-đun đó (chúng tra tên toàn cục lúc chạy); một yêu cầu đang xử lý đúng lúc đổi
có thể thấy lẫn bảng cũ và mới trong một lần quét (ponytail: giống knowledge_api, đổi vào lúc vắng người nếu cần chắc)."""
import logging
import sys
import threading
import time

from classification import data_files
from storage.ai_api import AiApi

NAMES = ("parsing_vocab", "thresholds", "app_screens")
POLL_SECONDS = 30
logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)  # PaddleOCR đặt lại mức log gốc về WARNING sau khi nạp model: không ghim thì dòng "Đã áp dụng" biến mất đúng lúc watcher chạy

_applied: dict[str, int] = {}  # tên -> phiên bản đang dùng (0 = mặc định trong data/)
_failed: dict[str, int] | None = None  # bộ phiên bản đã thử và hỏng, để không ghi lỗi lặp mỗi nhịp
_lock = threading.Lock()


def _active(api: AiApi) -> dict[str, tuple[int, dict]]:
    """Các tài liệu có bản đang chạy trong AI Service (tài liệu chưa ai kích hoạt thì vắng mặt)."""
    documents = {name: api.config_document(name) for name in NAMES}
    return {name: (version, content) for name, (version, content) in documents.items() if version > 0 and content is not None}


def _bind(vocab: dict, thresholds: dict, screens: dict) -> None:
    """Gắn giá trị vào các mô-đun: khóa ``parsing.extract._X`` = tên mô-đun + tên biến. Dựng hết trước, gán sau."""
    values = {key: data_files.build_table(key, entry) for key, entry in vocab.items()}
    values.update({key: entry["value"] for key, entry in thresholds.items() if not key.startswith("_")})
    values["parsing.app_screens._SPEC"] = screens  # chữ/hằng số theo giao diện từng app
    targets = []
    for key, value in values.items():
        module_name, attribute = key.rsplit(".", 1)
        module = sys.modules.get(module_name)
        if module is None:
            continue  # mô-đun chưa nạp trong tiến trình này: lúc nạp nó sẽ đọc data_files đã được ghi đè
        if not hasattr(module, attribute):
            raise KeyError(f"{key} không có trong mô-đun đang chạy")  # khóa lạ: hủy cả lần nạp
        targets.append((module, attribute, value))
    for module, attribute, value in targets:
        setattr(module, attribute, value)


def apply(vocab_doc: dict | None, thresholds_doc: dict | None, screens_doc: dict | None = None) -> None:
    """Áp dụng tài liệu (None = quay về mặc định trong data/). Khóa thiếu thì lấy mặc định."""
    defaults = {"parsing_vocab": data_files.load("parsing_vocab.json"), "thresholds": data_files.load("thresholds.json"), "app_screens": data_files.load("app_screens.json")}
    vocab = {**defaults["parsing_vocab"], **(vocab_doc or {})}
    thresholds = {**defaults["thresholds"], **(thresholds_doc or {})}
    screens = {**defaults["app_screens"], **(screens_doc or {})}
    _bind(vocab, thresholds, screens)
    data_files.override(vocab if vocab_doc is not None else None, thresholds if thresholds_doc is not None else None, screens if screens_doc is not None else None)


def reload(api: AiApi) -> dict[str, int]:
    """Nạp lại nếu bản đang chạy ở AI Service khác bản tiến trình này đang dùng. Trả về {tên: phiên bản mới} (rỗng nếu không có gì đổi)."""
    global _failed
    with _lock:
        active = _active(api)
        versions = {name: active.get(name, (0, None))[0] for name in NAMES}
        if all(versions[name] == _applied.get(name, 0) for name in NAMES) or versions == _failed:
            return {}
        try:
            apply(active.get("parsing_vocab", (0, None))[1], active.get("thresholds", (0, None))[1], active.get("app_screens", (0, None))[1])
        except Exception:  # noqa: BLE001 - bản hỏng không được làm sập dịch vụ: giữ bản đang chạy
            logger.exception("Không áp dụng được tài liệu cấu hình (phiên bản %s); giữ bản đang chạy", versions)
            _failed = versions
            return {}
        _failed = None
        changed = {name: version for name, version in versions.items() if version != _applied.get(name, 0)}
        _applied.update(versions)
        logger.info("Đã áp dụng tài liệu cấu hình: %s", changed)
        return changed


def start_watcher(api: AiApi, interval: float = POLL_SECONDS) -> threading.Thread:
    """Luồng nền kiểm tra định kỳ; không bao giờ thoát vì lỗi tạm thời (AI Service chưa lên hay đang khởi động lại)."""
    def loop() -> None:
        while True:
            time.sleep(interval)
            try:
                reload(api)
            except Exception:  # noqa: BLE001
                logger.warning("Chưa nạp được tài liệu cấu hình từ AI Service", exc_info=True)

    thread = threading.Thread(target=loop, daemon=True, name="config-watch")
    thread.start()
    return thread
