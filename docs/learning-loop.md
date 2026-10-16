# Vòng học của phân loại danh mục hóa đơn: cách vận hành

Mục tiêu: dùng nhãn do **người dùng** chốt (có đồng ý) để huấn luyện một mô hình nhóm hóa đơn, bật nó chỉ khi nó chứng minh được tốt hơn luật hiện tại.
Kế hoạch gốc: `docs/superpowers/plans/2026-10-13-learning-loop.md`. Mọi lệnh bên dưới chạy trong thư mục `ai-service/` của container.

## Nguyên tắc (không đổi)

- Mô hình **không bao giờ tự phục vụ**. `--train` chỉ tạo bản `candidate` (hoặc `rejected` kèm lý do); chỉ `--promote` mới bật.
- Nhãn huấn luyện chỉ đến từ người dùng xác nhận hoặc sửa. Câu trả lời của Gemini và bảng `bill_item_cache` chỉ là dự đoán, không bao giờ là nhãn.
- Chỉ chữ hóa đơn đã lọc dữ liệu cá nhân được lưu. Không ảnh, không âm thanh. Người dùng rút đồng ý thì mẫu của họ bị xóa.
- Mô hình chỉ trả lời ở `/bill/classify`, **khi luật không có câu trả lời** và xác suất ≥ 0,8; độ tin cậy trả về tối đa 0,8 như mọi kết quả hóa đơn.
  Hóa đơn thuốc, thương hiệu đã biết, cửa hàng người dùng đã chốt: luật/bộ nhớ quyết trước, mô hình không can thiệp.

## 0. Điều kiện để có dữ liệu

1. Người thử bật công tắc "Đóng góp dữ liệu huấn luyện" trong Hồ sơ của app (đồng ý riêng, mặc định tắt; tắt đi thì mẫu của họ bị xóa).
2. Họ quét hóa đơn như bình thường. Mẫu chờ nhãn vào `bill_training_samples`; khi họ lưu hoặc sửa danh mục, mẫu có nhãn cuối. Mẫu không bao giờ được lưu bị xóa sau 7 ngày.

## 1. Nhịp hằng tuần

```bash
docker compose exec ai-service python scripts/learning_report.py
```
Cho biết: số mẫu có nhãn, số cửa hàng khác nhau, theo danh mục, **độ đúng thật của luật** và của từng nguồn (Gemini, bộ nhớ, thương hiệu...) so với nhãn người dùng.
Dòng cuối là `SẴN SÀNG huấn luyện.` hoặc `CHƯA đủ`, kèm việc còn thiếu. Cổng tối thiểu (giả định chưa kiểm chứng, chỉnh khi có dữ liệu thật):
≥ 300 mẫu có nhãn, ≥ 30 cửa hàng khác nhau, ≥ 3 danh mục mỗi danh mục ≥ 20 mẫu.

Khi `SẴN SÀNG`:
```bash
docker compose exec ai-service python scripts/train_bill_model.py --train
```
In: `CANDIDATE` hoặc `REJECTED` + lý do. Mô hình chỉ thành candidate khi **cả ba** đúng:
1. đủ cổng mẫu ở trên;
2. độ chính xác kiểm tra chéo **theo cửa hàng** ≥ 0,85 (một cửa hàng không bao giờ nằm ở cả hai phía);
3. **hơn luật hiện tại** (luật + bộ nhớ, không tính Gemini) trên cùng các mẫu.

Hai số cần đọc kỹ trước khi bật: `coverage_at_threshold` (mô hình sẽ trả lời bao nhiêu phần hóa đơn) và `accuracy_at_threshold` (khi đã trả lời thì đúng bao nhiêu).
Mô hình chỉ trả lời lúc xác suất ≥ 0,8 nên số thứ hai mới là điều người dùng cảm nhận.

```bash
docker compose exec ai-service python scripts/train_bill_model.py --list              # các phiên bản và độ chính xác
docker compose exec ai-service python scripts/train_bill_model.py --promote <version> # bật (bản active cũ chuyển retired)
docker compose restart ai-service                                                      # nạp mô hình lúc khởi động
```
Chỉ bản `candidate` mới bật được; `rejected` hoặc mã sai thì lệnh báo lỗi và không đổi gì.

## 2. Quay về luật

```bash
docker compose exec ai-service python scripts/train_bill_model.py --retire
docker compose restart ai-service
```
Mô hình hỏng hoặc thiếu tệp cũng không làm sập dịch vụ: log `slug_model_load_failed` và chạy bằng luật.
Tệp mô hình nằm ở `ai-service/models/bill_slug/<version>.joblib` (thư mục `models` được gắn vào container ở `/models`, không nằm trong git).

## 3. Dọn dẹp và quyền người dùng

- Mẫu chờ nhãn quá 7 ngày được dịch vụ tự xóa mỗi giờ. Xem trước: `python scripts/purge_training_samples.py --days 7 --dry-run`.
- Xóa theo người dùng: khi người dùng rút đồng ý hoặc yêu cầu xóa tài khoản, backend gọi xóa theo `user_id_hash`; không cần làm tay.
- Mẫu đã có nhãn được giữ vì đó là dữ liệu huấn luyện đã được đồng ý.

## 4. Sau khi sửa code

Image ai-service chép toàn bộ thư mục (`COPY . .`): sửa code xong phải `docker compose build ai-service` rồi `up -d`, nếu không container vẫn chạy bản cũ
(và sẽ không có `slug_model.py`). Lưu ý bản build lấy cả thay đổi chưa commit trong cây làm việc.

## 5. Đo thử trên bộ dữ liệu hiện có (chỉ để báo cáo)

```bash
docker compose exec ai-service python scripts/eval_category_dataset.py
```
Dùng `category-classifier/*.csv` (sinh bằng `scripts/export_category_dataset.py`). Số đo hiện tại và cách đọc: `docs/evaluation.md`, mục "Mô hình nhóm hóa đơn, baseline 2026-10-16".

## Đã chạy thử

Toàn bộ quy trình trên (`learning_report` → `--train` → `--list` → `--promote` → promote lần hai bị từ chối → `--retire` → `purge --dry-run`) đã chạy thật một lần ngày 2026-10-16
trên một database tạm với 400 mẫu giả (đã xóa sau khi thử). Chưa chạy trên dữ liệu thật vì hiện **0 mẫu** huấn luyện.
