# stt-service

Audio → chữ (GIPFormer 1.5, ONNX, CPU). Engine lấy từ gói Voice Lab (`MODEL-NOTICE.md`, `GIPFORMER-LICENSE.txt`). Chỉ backend gọi, qua `Authorization: Bearer STT_SERVICE_API_KEY`; không mở cổng ra máy chủ.

- `POST /transcribe` (multipart `file`, ≤10 MiB, ≤60 giây) → `{text, duration_ms, engine, processing_ms}`. `text` đã hạ chữ thường.
- Model: đặt 4 file `encoder.onnx decoder.onnx joiner.onnx tokens.txt` vào `models/gipformer1.5-fp32/` (bị git bỏ qua). Tải: https://huggingface.co/g-group-ai-lab/gipformer1.5-68M-rnnt (revision `dd9227dcd8705c13f33bdbe59728d546ab94480f`).
- Lần gọi đầu nạp model (~2 giây). Một lượt tại một thời điểm; quá tải trả 429.
- Test: `pytest tests` (dùng engine giả, không cần model).
