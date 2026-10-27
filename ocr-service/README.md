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

## OCR Google Cloud Vision (đường chính)

Có `GOOGLE_VISION_API_KEY` (đặt trong `ocr-service/.env`, xem `.env.example`) thì `reading/google_vision.py` gọi Cloud Vision `DOCUMENT_TEXT_DETECTION` (gợi ý tiếng Việt)
trước; lỗi, hết hạn mức, Google không thấy chữ, hoặc chưa có khóa thì dùng OCR cục bộ (PaddleOCR + VietOCR) như cũ, lần quét không thất bại. Sau một lần lỗi, Google bị tạm
nghỉ `GOOGLE_VISION_COOLDOWN_SECONDS` giây. `FINMATE_OCR_ENGINE=local` ép OCR cục bộ, dùng để đo so sánh hai bên bằng cùng bộ ảnh (`benchmark/run_databill.py`, `run_items.py`).
Chữ Google không qua sửa dấu lạ hay VietOCR. **Ảnh hóa đơn rời hệ thống sang Google** (quyết định của chủ sản phẩm 2026-10-26, luôn gửi, chưa che dữ liệu cá nhân): màn đồng ý phải nói đúng điều này.

## Kiểm thử

```
docker run --rm -v "$PWD:/app" -w /app <image-ocr-service> sh -c "pip install -q -r requirements-dev.txt -c constraints.txt && python -m pytest -q tests"
```

## Dữ liệu

Không có DB và không kết nối DB nào: mọi dữ liệu dùng chung (bộ nhớ cửa hàng, thương hiệu và từ khóa đã học, bill đã gắn nhãn, lịch sử huấn luyện) đi qua API nội bộ
của `ai-service` (`/api/v1/ocr-store/*`, máy khách ở `storage/ai_api.py`), nên ai-service đổi bảng thì ocr-service không phải sửa. `ai-service` phải chạy trước (compose đặt `depends_on` healthy).
Cần `AI_SERVICE_URL` (compose đã đặt) và `AI_SERVICE_API_KEY` (cùng giá trị `INTERNAL_API_KEY` của ai-service, đặt trong `.env`); thiếu một trong hai thì dùng file cạnh code và bảng mặc định trong `data/*.json`.

Bộ nhớ cửa hàng theo `X-User-Hash` (backend gửi SHA-256 của user_id).

Nạp dữ liệu (`python scripts/db_setup.py --seed/--import/--gold/--synthetic --write`) chỉ chạy được sau `alembic upgrade head`; db_setup không tạo bảng.

**Thương hiệu và từ khóa mặc định** nằm ở `data/*.json` (lý do từng mục: `data/NOTES.md`), không viết cứng trong `.py`. Khi nối được AI Service, kiến thức của nó (`kb_brands`, `kb_keywords`) thắng: có dữ liệu ở đó thì nó thay bản mặc định. Loại `merchant`/`product` và thương hiệu do ai-service sở hữu và tự seed vào DB lúc khởi động (`app/bill/seed_defaults.py`); `data/*.json` ở đây là bản dự phòng khi không có DB, phải giống bản của ai-service (test `ai-service/tests/unit/test_bill_seed_defaults.py` bắt lệch). Từ điển câu chat không còn ở đây: nó thuộc ai-service (`ai-service/dictionaries/`, bản chạy thật là tài liệu `chat_dictionary` trong `config_documents`). Dùng `python scripts/seed_knowledge.py` (xem trước, liệt kê mục còn thiếu) rồi thêm `--write` nếu cần seed tay. Seed chỉ thêm, an toàn chạy lại ở mọi môi trường; mục đã sửa hoặc đã tắt (`is_active = false`) trong DB không bị ghi đè. Muốn bỏ một mục thì tắt chứ đừng xóa dòng (xóa rồi seed sẽ thêm lại).

**Quy tắc đọc bill và tách món** (bằng chứng số học thay cho ngưỡng tọa độ, ô gộp, tên món đầu, nhãn tổng rơi chữ, tách cổng với backend): xem `.context/ARCHITECTURE.md` §3.4.1. Sửa theo lớp lỗi và đo trên 58 ảnh (`benchmark/run_databill.py`, `run_items.py`), không sửa theo từng bill.

## Chưa làm

- Chưa có phân loại theo từng dòng hàng và bảng chia nhóm (cần đổi contract `/ocr`, phải được duyệt).
