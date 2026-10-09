# FinMate bill-api (OCR + phân loại bill). Build từ thư mục này:  docker build -t finmate-bill-api .
# ponytail: chưa thử build trên máy phát triển (Docker Desktop tắt lúc viết); build lần đầu cần Internet (pip + tải model PaddleOCR).
FROM python:3.10-slim

# thư viện hệ thống cho OpenCV/PaddleOCR (bản không có giao diện)
RUN apt-get update && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt constraints.txt ./
# constraints.txt ghim đúng các phiên bản đã chạy thử (numpy, opencv, paddle...) để image giống môi trường đã kiểm tra
RUN pip install --no-cache-dir -r requirements.txt -c constraints.txt

# Tải sẵn model PaddleOCR vào image (container khởi động không cần Internet, lần quét đầu không chậm).
# Đặt TRƯỚC khi chép code để sửa code rồi build lại không phải tải lại model. Tham số phải giống ocr._engine().
# "import numpy, cv2" đứng trước: nạp PaddleOCR đầu tiên bị lỗi zlib khi import pyclipper (xung đột thư viện nén dùng chung).
RUN python -c "import numpy, cv2; from paddleocr import PaddleOCR; PaddleOCR(use_angle_cls=True, lang='vi', show_log=False)"

COPY . .

ENV PYTHONUNBUFFERED=1
EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8000/health', timeout=4).status == 200 else 1)"
# 1 worker: bộ nhớ lượt quét tạm (scan_id) nằm trong RAM của tiến trình
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
