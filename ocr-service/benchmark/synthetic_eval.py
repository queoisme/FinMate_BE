"""So sánh mô hình danh mục: chỉ hóa đơn thật vs thêm hóa đơn giả. Luôn kiểm tra trên hóa đơn THẬT (nhóm theo cửa hàng).

Chạy:  .\.venv\Scripts\python.exe benchmark\synthetic_eval.py [số_giả_mỗi_danh_mục ...]
"""
import sys
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
from sklearn.model_selection import GroupKFold

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(1, str(Path(__file__).resolve().parents[1] / "scripts"))  # synthetic_bills
sys.stdout.reconfigure(encoding="utf-8")

from classification.category_model import _group_key, _pipeline, record_text  # noqa: E402
from storage.dataset import Dataset  # noqa: E402
from synthetic_bills import generate  # noqa: E402

real = Dataset(Path(__file__).resolve().parents[1] / "dataset.jsonl").read_all()
texts, labels = np.array([record_text(r) for r in real]), np.array([r["category"] for r in real])
groups = [_group_key(r) for r in real]
print(f"{len(real)} hóa đơn thật, {len(set(groups))} cửa hàng; đoán toàn 'groceries' sẽ đúng {Counter(labels).most_common(1)[0][1] / len(real):.0%}\n")


def cv(extra: list[dict]) -> tuple[float, dict]:
    et, el = [record_text(r) for r in extra], [r["category"] for r in extra]
    ok, per = 0, defaultdict(lambda: [0, 0])
    for tr, te in GroupKFold(n_splits=min(5, len(set(groups)))).split(texts, labels, groups):
        m = _pipeline().fit(list(texts[tr]) + et, list(labels[tr]) + el)
        for y, p in zip(labels[te], m.predict(texts[te])):
            per[y][1] += 1
            per[y][0] += y == p
            ok += y == p
    return ok / len(real), per


def show(name: str, extra: list[dict]) -> None:
    acc, per = cv(extra)
    detail = ", ".join(f"{c} {a}/{n}" for c, (a, n) in sorted(per.items()))
    print(f"{name:42} {acc:.0%}   [{detail}]")


show("chỉ hóa đơn thật", [])
for n in [int(a) for a in sys.argv[1:]] or [20, 40, 80]:
    show(f"+ giả {n}/danh mục (có tên thương hiệu)", generate(n, use_brands=True))
    show(f"+ giả {n}/danh mục (chỉ tên cửa hàng chung)", generate(n, use_brands=False))

# ảnh giả đã qua OCR thật (synthetic_images.py): chữ bị hỏng giống bill thật
ocr_fake = Dataset(Path(__file__).resolve().parents[1] / "dataset_synthetic_ocr.jsonl").read_all()
if ocr_fake:
    show(f"+ ảnh giả qua OCR ({len(ocr_fake)} ảnh)", ocr_fake)
    show("+ ảnh giả qua OCR + bill giả chữ sạch", ocr_fake + generate(40, use_brands=True))
