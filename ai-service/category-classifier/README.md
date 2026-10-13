# category-classifier

Tập dữ liệu để huấn luyện và đo bộ phân loại danh mục (mô hình học, thay cho luật khi đủ dữ liệu). Ba tệp, cột `text,label,merchant_key,labeled_by`:

- `train.csv`, `validation.csv`, `test.csv`: chia theo CỬA HÀNG (một cửa hàng chỉ nằm ở một tập), cố định theo băm nên chạy lại cho cùng kết quả.
- `label` là slug backend (`food`, `transport`, ...). `labeled_by`: `user` (người dùng chốt), `gold`, `imported`, `synthetic_ocr`; khi đo nên tách `synthetic_ocr` ra.

**Các tệp CSV không nằm trong git** vì chứa chữ hóa đơn. Sinh lại từ database của ai-service:

```
python scripts/export_category_dataset.py
```

Cổng chặn trước khi dùng mô hình học, và cách thu nhãn thật có đồng ý của người dùng: `docs/superpowers/plans/2026-10-13-learning-loop.md`.
