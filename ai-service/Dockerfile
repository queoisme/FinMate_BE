FROM python:3.11-slim

WORKDIR /src

# Tesseract + dữ liệu tiếng Việt. Thiếu gói `-vie` thì OCR vẫn chạy nhưng đọc hóa đơn
# tiếng Việt ra ký tự rác, và không có lỗi nào báo — chỉ là text vô nghĩa.
RUN apt-get update \
    && apt-get install --no-install-recommends -y tesseract-ocr tesseract-ocr-vie \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

ENV LOG_LEVEL=INFO
EXPOSE 8000

# Chạy migration trước khi phục vụ, cùng cách backend gọi Database.Migrate() trong
# Program.cs — nếu không, container mới lên sẽ 500 trên mọi request chạm DB.
CMD ["sh", "-c", "alembic upgrade head && exec uvicorn app.main:app --host 0.0.0.0 --port 8000"]
