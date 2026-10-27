"""Lưu dữ liệu quét bill qua API của AI Service thay cho ``dataset.jsonl`` và ``merchant_memory.json`` (bản trước kết nối thẳng PostgreSQL: ``storage_pg.py``).

Bật bằng ``AI_SERVICE_URL`` + ``AI_SERVICE_API_KEY`` (xem ``ai_api.py``); không có thì vẫn dùng file như cũ.
FINMATE_USER_HASH: mã người dùng mặc định khi yêu cầu không mang header ``X-User-Hash``; mặc định "local" cho máy dev (được băm thành SHA-256 trước khi gửi).
"""
import hashlib
import os
import time
from pathlib import Path

from parsing.extract import CATEGORIES
from storage.ai_api import AiApi, api_from_env, current_user, who  # noqa: F401  (current_user: main.bind_user đặt người dùng của yêu cầu)
from storage.dataset import Dataset
from storage.memory import MerchantMemory, _key
from classification import data_files

# DB lưu cửa hàng người dùng đã chốt theo slug backend (``user_merchant_memory``), ocr-service nghĩ theo 9 nhóm: đổi qua bảng dùng chung với ai-service (category_groups.json).
# Chiều ngược lại không đơn trị (food là dining hay groceries): lấy nhóm đứng đầu danh sách; slug backend không có nhóm nào (education, family...) thì bỏ qua, để ocr-service tự phân loại.
BACKEND_SLUG = data_files.load("category_groups.json")["backend_slug"]
GROUP_OF: dict[str, str] = {}
for _group in CATEGORIES:
    GROUP_OF.setdefault(BACKEND_SLUG[_group], _group)

_MEMORY_TTL = 3.0  # giây: một lượt quét hỏi bộ nhớ nhiều lần (khớp mờ, tên đẹp), không cần gọi AI Service mỗi lần


class ApiMerchantMemory(MerchantMemory):
    """Cùng luật khớp mờ như bản file; chỉ khác chỗ đọc/ghi."""

    def __init__(self, api: AiApi, user_hash: str):
        self._api, self._user = api, user_hash
        self._cache: dict[str, tuple[float, dict]] = {}

    def _load(self) -> dict:
        user = who(self._user)
        cached = self._cache.get(user)
        if cached and time.monotonic() - cached[0] < _MEMORY_TTL:
            return cached[1]
        rows = self._api.merchant_memory(user)
        data = {r["merchant_key"]: {"category": GROUP_OF[r["category_slug"]], **({"name": r["display_name"]} if r["display_name"] else {})}
                for r in rows if r["category_slug"] in GROUP_OF}
        self._cache[user] = (time.monotonic(), data)
        return data

    def _save(self, key: str, entry: dict, data: dict) -> None:
        user = who(self._user)
        self._api.put_merchant_memory(user, key, entry.get("name"), BACKEND_SLUG[entry["category"]])
        self._cache.pop(user, None)  # lần đọc kế tiếp thấy ngay điều vừa ghi


class ApiDataset:
    """Cùng giao diện append/read_all như Dataset (file jsonl)."""

    def __init__(self, api: AiApi, user_hash: str):
        self._api, self._user = api, user_hash

    def append(self, record: dict) -> None:
        lines = list(record.get("lines") or [])
        merchant, total, category = record.get("merchant") or "", record.get("total_amount"), record["category"]
        if category not in CATEGORIES:
            raise ValueError(f"Danh mục không hợp lệ: {category}")
        content_hash = hashlib.sha256("\n".join([_key(merchant), str(total), *lines]).encode("utf-8")).hexdigest()
        self._api.append_labeled({
            "user_id_hash": who(self._user), "content_hash": content_hash, "merchant": merchant, "lines": lines, "total_amount": total,
            "category_slug": category, "label_source": record.get("label_source") or "user", "ts": record.get("ts"), "is_gold": bool(record.get("is_gold")),
        })

    def read_all(self) -> list[dict]:
        """Toàn bộ bill đã gán nhãn của MỌI người dùng (dùng cho huấn luyện chung)."""
        return self._api.list_labeled()


def open_stores(base_dir: Path):
    """(bộ nhớ cửa hàng, dataset): qua API của AI Service nếu có ``AI_SERVICE_URL``, không thì file cạnh code như trước."""
    api = api_from_env()
    if api is None:
        return MerchantMemory(base_dir / "merchant_memory.json"), Dataset(base_dir / "dataset.jsonl")
    user = os.environ.get("FINMATE_USER_HASH", "local")
    return ApiMerchantMemory(api, user), ApiDataset(api, user)
