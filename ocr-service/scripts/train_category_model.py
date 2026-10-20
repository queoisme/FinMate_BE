"""Huấn luyện mô hình danh mục từ dataset.jsonl (hoặc từ PostgreSQL khi có FINMATE_DATABASE_URL).

Chạy:  .\.venv\Scripts\python.exe scripts/train_category_model.py [--synthetic]   (--synthetic: thêm dataset_synthetic.jsonl vào phần huấn luyện)
Chỉ tạo category_model.joblib khi đủ dữ liệu và độ chính xác kiểm tra chéo đạt ngưỡng.
Có DB: mỗi lần chạy (kể cả không đạt) được ghi vào bill_training_jobs / bill_model_versions / evaluation_* (xem training_pg.py).
Mô hình đạt ngưỡng cũng được lưu thành bản có phiên bản trong category_models/ để quay lại được.
Sau khi tạo, khởi động lại API để nạp mô hình.
"""
import os
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent  # thư mục dịch vụ (script nằm trong scripts/): nơi các module và file dữ liệu ở
sys.path.insert(0, str(HERE))

from classification.category_model import MIN_CV_ACCURACY, MIN_RECORDS, CategoryModel, train  # noqa: E402
from storage.dataset import Dataset  # noqa: E402
from storage.storage_pg import open_stores  # noqa: E402
from storage.training_pg import new_version, open_training_recorder  # noqa: E402


def run(dataset_path, model_path: Path, synthetic_path: Path | None = None, recorder=None) -> int:
    rows = (dataset_path if hasattr(dataset_path, "read_all") else Dataset(dataset_path)).read_all()  # đường dẫn file, hoặc kho dữ liệu (PostgreSQL)
    fake = Dataset(synthetic_path).read_all() if synthetic_path else []
    # bill giả (label_source bắt đầu bằng "synthetic") chỉ được vào phần huấn luyện, không bao giờ vào phần đo
    fake += [r for r in rows if str(r.get("label_source", "")).startswith("synthetic")]
    rows = [r for r in rows if not str(r.get("label_source", "")).startswith("synthetic")]
    version = new_version()
    job = recorder.start({"synthetic_rows": len(fake), "real_rows": len(rows), "min_records": MIN_RECORDS, "min_cv_accuracy": MIN_CV_ACCURACY}) if recorder else None
    try:
        report = train(rows, synthetic=fake)
    except Exception as e:  # noqa: BLE001 - ghi lại lần chạy lỗi rồi báo lỗi như cũ
        if recorder:
            recorder.fail(job, f"{type(e).__name__}: {e}")
        raise
    if fake:
        print(f"Có thêm {len(fake)} hóa đơn giả (chỉ để huấn luyện; số đo bên dưới vẫn tính trên hóa đơn thật)")
    print(f"Số hóa đơn có nhãn : {report.n_records} (cần >= {MIN_RECORDS})")
    print(f"Theo danh mục      : {report.classes}")
    if report.cv_accuracy is not None:
        print(f"Chính xác kiểm tra chéo (nhóm theo cửa hàng): {report.cv_accuracy:.0%} (ngưỡng {MIN_CV_ACCURACY:.0%})")
    artifact = ""
    if report.trained:
        versioned = model_path.parent / "category_models" / f"bill_category-{version}.joblib"
        versioned.parent.mkdir(exist_ok=True)
        CategoryModel(report.model).save(versioned)
        shutil.copyfile(versioned, model_path)  # bản đang chạy = bản có phiên bản mới nhất
        artifact = str(versioned)
    if recorder:
        recorder.finish(job, report, version, artifact)
        print(f"Đã ghi lần huấn luyện vào DB (phiên bản {version}, {'active' if report.trained else 'rejected'}).")
    if not report.trained:
        print(f"CHƯA bật mô hình: {report.reason}")
        return 1
    print(f"Đạt. Đã lưu mô hình: {model_path} (bản có phiên bản: {artifact})")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    use_fake = "--synthetic" in sys.argv
    has_db = bool(os.environ.get("FINMATE_DATABASE_URL"))
    real_data = open_stores(HERE)[1] if has_db else HERE / "dataset.jsonl"  # có DB thì học từ DB
    sys.exit(run(real_data, HERE / "category_model.joblib", HERE / "dataset_synthetic.jsonl" if use_fake else None, open_training_recorder() if has_db else None))
