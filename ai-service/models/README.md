# Model artifacts

Thư mục này được mount vào container tại `MODEL_REGISTRY_PATH` (`/models`).
Cấu trúc: `<stage>/<version>/model.joblib`, ví dụ `classifier/1.0.0/model.joblib`.

Artifact **không commit vào git** (xem `.gitignore`): nó là sản phẩm build, được sinh lại
bằng `python scripts/train.py --stage <stage>`. Thư mục trống nghĩa là chưa có model nào —
pipeline chạy bằng luật, đó là trạng thái hợp lệ.

Nguồn sự thật về model nào đang phục vụ là bảng `model_versions` trong AI DB, không phải
file trên đĩa: một artifact không có dòng `status='active'` tương ứng sẽ bị bỏ qua. Đó là
thứ phân biệt "đã train" với "đã promote".
