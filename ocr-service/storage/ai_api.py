"""Máy khách HTTP của API lưu trữ của AI Service (``/api/v1/ocr-store`` và ``/api/v1/admin/config``): lối DUY NHẤT để ocr-service đọc/ghi dữ liệu dùng chung.

Trước đây ocr-service kết nối thẳng vào DB của AI Service và tự chạy SQL, nên mỗi lần AI Service đổi bảng là phải sửa và triển khai cả hai cùng lúc. Giờ ocr-service
không biết tên bảng nào: chỉ biết các endpoint này.

Bật bằng hai biến môi trường: ``AI_SERVICE_URL`` (vd. ``http://ai-service:8000``) và ``AI_SERVICE_API_KEY`` (cùng giá trị ``INTERNAL_API_KEY`` của ai-service).
Thiếu một trong hai thì ocr-service chạy chế độ file như cũ (``merchant_memory.json``, ``dataset.jsonl``) và dùng ``data/*.json`` làm kiến thức.

Lỗi mạng hay lỗi HTTP đều thành ``AiApiError`` (có ``status``, ``code``): người gọi quyết định bỏ qua (ghi bill lạ) hay để lan ra (lưu nhãn của người dùng).
Mã người dùng luôn là SHA-256 hex 64 ký tự: mã không đúng dạng (như ``local`` mặc định cho máy dev) được băm trước khi gửi. Không log chữ hóa đơn.
"""
from __future__ import annotations

import hashlib
import os
import re
from contextvars import ContextVar
from typing import Any

import httpx

_HEX64 = re.compile(r"[0-9a-f]{64}")
_PAGE = 500  # số bill mỗi trang khi đọc toàn bộ dữ liệu huấn luyện

# Người dùng của yêu cầu đang xử lý (backend truyền mã băm trong header X-User-Hash; xem main.bind_user). Không có thì dùng mã mặc định.
current_user: ContextVar[str | None] = ContextVar("finmate_user", default=None)


def who(default: str) -> str:
    """Mã băm người dùng của yêu cầu, hoặc ``default``; luôn trả SHA-256 hex 64 ký tự."""
    value = current_user.get() or default
    return value if _HEX64.fullmatch(value) else hashlib.sha256(value.encode("utf-8")).hexdigest()


class AiApiError(RuntimeError):
    def __init__(self, status: int, code: str, message: str) -> None:
        super().__init__(f"{status} {code}: {message}")
        self.status, self.code, self.message = status, code, message


class AiApi:
    def __init__(self, base_url: str, api_key: str, *, client: httpx.Client | None = None, timeout: float = 15.0) -> None:
        self._client = client or httpx.Client(base_url=base_url.rstrip("/"), headers={"Authorization": f"Bearer {api_key}"}, timeout=timeout)

    def _call(self, method: str, path: str, *, params: dict | None = None, json: Any = None) -> Any:
        try:
            response = self._client.request(method, path, params=params, json=json)
        except httpx.HTTPError as error:
            raise AiApiError(0, "unreachable", f"Không gọi được AI Service ({type(error).__name__})") from error
        if response.status_code >= 400:
            try:
                body = response.json()
            except ValueError:
                body = {}
            detail = body.get("error") or body.get("detail") or {}
            if isinstance(detail, dict):
                raise AiApiError(response.status_code, str(detail.get("code", "error")), str(detail.get("message", response.text[:200])))
            raise AiApiError(response.status_code, "error", str(detail)[:200])
        return response.json()

    # ------------------------------------------------------------------ kiến thức
    def knowledge(self, include_inactive: bool = False) -> dict:
        return self._call("GET", "/api/v1/ocr-store/knowledge", params={"include_inactive": include_inactive})

    def seed_knowledge(self, brands: list[dict], keywords: list[dict]) -> dict:
        return self._call("POST", "/api/v1/ocr-store/knowledge/seed", json={"brands": brands, "keywords": keywords})

    def propose_brand(self, brand: dict) -> bool:
        return self._call("POST", "/api/v1/ocr-store/brands/vote", json=brand)["created"]

    def activate_voted_brand(self, brand_key: str) -> bool:
        return self._call("POST", "/api/v1/ocr-store/brands/activate-vote", json={"merchant_key": brand_key})["activated"]

    def merchant_consensus(self) -> list[dict]:
        return self._call("GET", "/api/v1/ocr-store/consensus/merchants")["items"]

    # ------------------------------------------------------------------ cửa hàng người dùng đã chốt
    def merchant_memory(self, user_id_hash: str) -> list[dict]:
        return self._call("GET", "/api/v1/ocr-store/merchant-memory", params={"user_id_hash": user_id_hash})["items"]

    def put_merchant_memory(self, user_id_hash: str, merchant_key: str, display_name: str | None, category_slug: str) -> None:
        self._call("PUT", "/api/v1/ocr-store/merchant-memory",
                   json={"user_id_hash": user_id_hash, "merchant_key": merchant_key, "display_name": display_name, "category_slug": category_slug})

    # ------------------------------------------------------------------ bill đã gắn nhãn, bộ chuẩn
    def append_labeled(self, sample: dict) -> None:
        self._call("POST", "/api/v1/ocr-store/labeled-samples", json=sample)

    def list_labeled(self) -> list[dict]:
        """Mọi bill đã gắn nhãn của ocr-service (mọi người dùng), đọc theo trang."""
        items: list[dict] = []
        while True:
            page = self._call("GET", "/api/v1/ocr-store/labeled-samples", params={"limit": _PAGE, "offset": len(items)})
            items.extend(page["items"])
            if not page["items"] or len(items) >= page["total"]:
                return items

    def count_labeled_by(self, label_source: str) -> int:
        return self._call("GET", "/api/v1/ocr-store/labeled-samples/count", params={"label_source": label_source})["count"]

    def claim_gold(self, total: int, category_slug: str, dry_run: bool = False) -> str:
        return self._call("POST", "/api/v1/ocr-store/gold/claim", json={"total": total, "category_slug": category_slug, "dry_run": dry_run})["result"]

    def gold_merchant_names(self) -> dict:
        return self._call("GET", "/api/v1/ocr-store/gold/merchant-names")

    def rename_gold(self, old: str, new: str) -> int:
        return self._call("POST", "/api/v1/ocr-store/gold/rename", json={"old": old, "new": new})["updated"]

    # ------------------------------------------------------------------ bill lạ chờ duyệt
    def record_unknown_scan(self, scan: dict) -> None:
        self._call("POST", "/api/v1/ocr-store/unknown-scans", json=scan)

    def label_unknown_scans(self, merchant_key: str) -> int:
        return self._call("POST", "/api/v1/ocr-store/unknown-scans/label", json={"merchant_key": merchant_key})["updated"]

    # ------------------------------------------------------------------ lịch sử huấn luyện
    def start_training(self, stage: str, params: dict) -> str:
        return self._call("POST", "/api/v1/ocr-store/training/jobs", json={"stage": stage, "params": params})["job_id"]

    def fail_training(self, job_id: str, error: str) -> None:
        self._call("POST", f"/api/v1/ocr-store/training/jobs/{job_id}/fail", json={"error": error[:2000]})

    def finish_training(self, job_id: str, result: dict) -> str:
        return self._call("POST", f"/api/v1/ocr-store/training/jobs/{job_id}/finish", json=result)["model_version_id"]

    # ------------------------------------------------------------------ tài liệu cấu hình (admin sửa ở AI Service)
    def config_document(self, name: str) -> tuple[int, dict | None]:
        """(phiên bản, nội dung) của tài liệu đang chạy; ``(0, None)`` khi chưa ai kích hoạt bản nào (dùng mặc định trong ``data/``)."""
        document = self._call("GET", f"/api/v1/admin/config/documents/{name}")
        if document.get("source") != "db":
            return 0, None
        return int(document["version"]), document["content"]


def api_from_env() -> AiApi | None:
    """Máy khách nếu có ``AI_SERVICE_URL`` và ``AI_SERVICE_API_KEY``, không thì None (chế độ file)."""
    url, key = os.environ.get("AI_SERVICE_URL"), os.environ.get("AI_SERVICE_API_KEY")
    return AiApi(url, key) if url and key else None
