# ocr-service

Đọc hóa đơn từ ảnh: PaddleOCR (tìm chữ) + VietOCR (đọc chữ, ONNX), rồi luật bóc tên cửa hàng, tổng tiền và
danh mục. Chạy riêng một dịch vụ vì PaddlePaddle quá nặng để đưa vào image của `ai-service` (xem
`.context/TECH_STACK.md` §OCR). **Chỉ gọi được từ ai-service** qua `Authorization: Bearer {OCR_SERVICE_API_KEY}`;
không mở cổng ra máy chủ trong `docker-compose.yml`.

## Luồng

```
App → backend (ScanReceiptCommandHandler) → ocr-service POST /analyze-bill   (ảnh → tổng tiền, tên, ngày, chữ đã lọc)
                                           → ai-service POST /api/v1/bill/classify   (chữ đã lọc → danh mục)
```

Backend điều phối và đổi hai câu trả lời thành đúng contract `/transactions/scan-receipt` cũ, nên app không phải sửa gì.
ocr-service tự phân loại nữa nhưng backend chỉ dùng kết quả đó khi là bộ nhớ cửa hàng của người dùng (`category_source = memory`);
còn lại danh mục do `ai-service` quyết. Không đặt `OCR_SERVICE_URL` cho backend thì quét qua `POST /api/v1/ocr` của ai-service như trước
(ai-service gọi ocr-service, bảng quy đổi danh mục ở `ai-service/app/core/category_map.py`).

Ảnh chỉ đi qua bộ nhớ. Phản hồi `lines` là chữ ĐÃ lọc dữ liệu cá nhân (điện thoại, email, tên khách, tài khoản).

## Chạy

```
cp .env.example .env          # đặt OCR_SERVICE_API_KEY (giống ai-service/.env)
docker compose up -d --build ocr-service ai-service
```

Model VietOCR **không nằm trong git** (3 file ONNX, ~86MB): đặt vào `models/vietocr/{cnn,encoder,decoder}.onnx`
trước khi build image.

## Kiểm thử

```
docker run --rm -v "$PWD:/app" -w /app <image-ocr-service> sh -c "pip install -q -r requirements-dev.txt -c constraints.txt && python -m pytest -q tests"
```

## Dữ liệu

Không có DB riêng: dùng chung `finmate_ai` với `ai-service` (các bảng `bill_*`, do alembic `0006` của ai-service tạo, nên `ai-service` phải
chạy trước: compose đặt `depends_on: ai-service` healthy). Gồm bộ nhớ cửa hàng, thương hiệu và từ khóa đã học, bill đã gắn nhãn, lịch sử huấn luyện.
`FINMATE_DATABASE_URL` do compose đặt; chạy ngoài docker mà bỏ trống thì dùng file cạnh code và bảng mặc định trong code.

Bộ nhớ cửa hàng theo `X-User-Hash` (backend gửi SHA-256 của user_id).

Nạp dữ liệu (`python scripts/db_setup.py --seed/--import/--gold/--synthetic --write`) chỉ chạy được sau `alembic upgrade head`; db_setup không tạo bảng.

## Chưa làm

- Chưa có phân loại theo từng dòng hàng và bảng chia nhóm (cần đổi contract `/ocr`, phải được duyệt).
