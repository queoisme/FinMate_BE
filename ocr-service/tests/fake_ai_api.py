"""AI Service giả trong bộ nhớ cho test của ocr-service: cùng giao diện với ``storage.ai_api.AiApi``, mô phỏng đúng hành vi của ``/api/v1/ocr-store`` (đã được test thật ở
ai-service: tests/integration/test_ocr_store.py). ``test_ai_api.py`` giữ hai bên không lệch nhau (cùng tên hàm, cùng chữ ký).

Trạng thái để test đọc thẳng: ``brands``, ``keywords``, ``memory``, ``samples``, ``events``, ``unknown``, ``consensus``, ``documents``, ``jobs``, ``versions``, ``metrics``.
"""
from __future__ import annotations

import itertools

from storage.ai_api import AiApiError
from storage.storage_api import BACKEND_SLUG

EVENT_SOURCE = {"user": "user", "gold": "gold", "imported": "import", "synthetic_ocr": "system"}
EXPENSE_SLUGS = set(BACKEND_SLUG.values())  # slug backend của khoản chi: AI Service từ chối thu nhập và nhóm cũ ở bộ nhớ cửa hàng


class FakeAiApi:
    def __init__(self) -> None:
        self.brands: dict[str, dict] = {}
        self.keywords: list[dict] = []
        self.memory: dict[tuple[str, str], dict] = {}
        self.samples: dict[str, dict] = {}  # content_hash -> bill (dict theo thứ tự thêm vào)
        self.events: list[tuple[str, str]] = []  # (category_slug, source) của mỗi lần đặt nhãn
        self.unknown: dict[str, dict] = {}
        self.consensus: list[dict] = []  # test đặt trực tiếp: [{"merchant_key", "category_slug", "users"}]
        self.documents: dict[str, tuple[int, dict]] = {}  # tên -> (phiên bản, nội dung) đang chạy
        self.jobs: dict[str, dict] = {}
        self.versions: list[dict] = []
        self.metrics: list[tuple] = []
        self._ids = itertools.count(1)
        self._clock = itertools.count(1)

    # ------------------------------------------------------------------ kiến thức
    def knowledge(self, include_inactive: bool = False) -> dict:
        brands = [{**b, "brand_key": key} for key, b in sorted(self.brands.items()) if include_inactive or b["is_active"]]
        keywords = [k for k in sorted(self.keywords, key=lambda k: (k["category_priority"], k["category_slug"], k["keyword"])) if include_inactive or k["is_active"]]
        return {"brands": brands, "keywords": keywords}

    def seed_knowledge(self, brands: list[dict], keywords: list[dict]) -> dict:
        added = {"brands": 0, "keywords": 0}
        for b in brands:
            if b["brand_key"] not in self.brands:
                self.brands[b["brand_key"]] = {"category_slug": b["category_slug"], "display_name": b["display_name"], "whole_word": b.get("whole_word", False), "is_active": True, "source": "seed"}
                added["brands"] += 1
        have = {(k["kind"], k["keyword"]) for k in self.keywords}
        for k in keywords:
            if (k["kind"], k["keyword"]) not in have:
                self.keywords.append({"kind": k["kind"], "category_slug": k["category_slug"], "keyword": k["keyword"], "category_priority": k.get("category_priority", 100), "is_active": True})
                added["keywords"] += 1
        return added

    def propose_brand(self, brand: dict) -> bool:
        if brand["brand_key"] in self.brands:
            return False
        self.brands[brand["brand_key"]] = {"category_slug": brand["category_slug"], "display_name": brand["display_name"], "whole_word": brand.get("whole_word", False),
                                           "is_active": brand.get("is_active", False), "source": "vote"}
        return True

    def activate_voted_brand(self, brand_key: str) -> bool:
        brand = self.brands.get(brand_key)
        if brand and brand["source"] == "vote" and not brand["is_active"]:
            brand["is_active"] = True
            return True
        return False

    def merchant_consensus(self) -> list[dict]:
        return list(self.consensus)

    # ------------------------------------------------------------------ cửa hàng người dùng đã chốt
    def merchant_memory(self, user_id_hash: str) -> list[dict]:
        return [{"merchant_key": key, "category_slug": v["category_slug"], "display_name": v["display_name"]} for (user, key), v in self.memory.items() if user == user_id_hash]

    def put_merchant_memory(self, user_id_hash: str, merchant_key: str, display_name: str | None, category_slug: str) -> None:
        if category_slug not in EXPENSE_SLUGS:
            raise AiApiError(422, "invalid_category", "Danh mục phải là một khoản chi của backend")
        old = self.memory.get((user_id_hash, merchant_key.lower()), {})
        self.memory[(user_id_hash, merchant_key.lower())] = {"category_slug": category_slug, "display_name": display_name or old.get("display_name")}

    # ------------------------------------------------------------------ bill đã gắn nhãn, bộ chuẩn
    def append_labeled(self, sample: dict) -> None:
        old = self.samples.get(sample["content_hash"], {})
        self.samples[sample["content_hash"]] = {**sample, "is_gold": bool(old.get("is_gold") or sample.get("is_gold")), "order": old.get("order", next(self._clock))}
        self.events.append((sample["category_slug"], EVENT_SOURCE[sample["label_source"]]))

    def list_labeled(self) -> list[dict]:
        return [{"ts": str(s.get("ts") or ""), "lines": s["lines"], "merchant": s["merchant"], "category": s["category_slug"], "label_source": s["label_source"], "total_amount": s["total_amount"]}
                for s in sorted(self.samples.values(), key=lambda s: s["order"])]

    def count_labeled_by(self, label_source: str) -> int:
        return sum(s["label_source"] == label_source for s in self.samples.values())

    def claim_gold(self, total: int, category_slug: str, dry_run: bool = False) -> str:
        rows = [s for s in self.samples.values() if s["total_amount"] == total and s["category_slug"] == category_slug and s["label_source"] in ("imported", "gold")]
        if any(s["is_gold"] for s in rows):
            return "already"
        if len(rows) == 1:
            if not dry_run:
                rows[0]["is_gold"] = True
            return "marked"
        return "missing"

    def gold_merchant_names(self) -> dict:
        known = sorted({s["merchant"] for s in self.samples.values() if s["label_source"] != "gold" and s["merchant"]})
        gold = sorted({s["merchant"] for s in self.samples.values() if s["label_source"] == "gold" and s["merchant"]})
        return {"known": known, "gold": gold}

    def rename_gold(self, old: str, new: str) -> int:
        changed = [s for s in self.samples.values() if s["label_source"] == "gold" and s["merchant"] == old]
        for s in changed:
            s["merchant"] = new
        return len(changed)

    # ------------------------------------------------------------------ bill lạ chờ duyệt
    def record_unknown_scan(self, scan: dict) -> None:
        old = self.unknown.get(scan["content_hash"])
        if old:
            old["seen_count"] += 1
            old["last"] = next(self._clock)
        else:
            self.unknown[scan["content_hash"]] = {**scan, "seen_count": 1, "status": "new", "last": next(self._clock)}

    def label_unknown_scans(self, merchant_key: str) -> int:
        rows = [u for u in self.unknown.values() if u["merchant_key"] == merchant_key and u["status"] == "new"]
        for u in rows:
            u["status"] = "labeled"
        return len(rows)

    def pending_unknown(self) -> list[dict]:
        """Bill lạ đang chờ xem (status = 'new'), thấy nhiều lần nhất trước (người duyệt xem thẳng bảng, không qua API của ocr-service)."""
        rows = [u for u in self.unknown.values() if u["status"] == "new"]
        return [{"merchant": u["merchant_read"], "seen": u["seen_count"]} for u in sorted(rows, key=lambda u: (-u["seen_count"], -u["last"]))]

    # ------------------------------------------------------------------ lịch sử huấn luyện
    def start_training(self, stage: str, params: dict) -> str:
        job_id = f"job-{next(self._ids)}"
        self.jobs[job_id] = {"stage": stage, "status": "running", "params": params, "error": None, "sample_count": None}
        return job_id

    def fail_training(self, job_id: str, error: str) -> None:
        self.jobs[job_id].update(status="failed", error=error[:2000])

    def finish_training(self, job_id: str, result: dict) -> str:
        self.jobs[job_id].update(status="succeeded", sample_count=result["n_records"])
        if result["trained"]:
            for v in self.versions:
                if v["status"] == "active":
                    v["status"] = "retired"
        version = {"version": result["version"], "status": "active" if result["trained"] else "rejected", "artifact_path": result["artifact_path"] if result["trained"] else "",
                   "cv_accuracy": result["cv_accuracy"], "macro_f1": result["macro_f1"], "classes": result["classes"], "reason": result["reason"], "job": job_id}
        self.versions.append(version)
        if result["cv_accuracy"] is not None:
            self.metrics.extend((result["version"], label, *values) for label, values in result["per_class"].items())
        return f"version-{len(self.versions)}"

    # ------------------------------------------------------------------ tài liệu cấu hình
    def config_document(self, name: str) -> tuple[int, dict | None]:
        return self.documents.get(name, (0, None))
