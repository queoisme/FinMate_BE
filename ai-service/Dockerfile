FROM python:3.11-slim

WORKDIR /src

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

ENV LOG_LEVEL=INFO
EXPOSE 8000

# Chạy migration trước khi phục vụ, cùng cách backend gọi Database.Migrate() trong
# Program.cs — nếu không, container mới lên sẽ 500 trên mọi request chạm DB.
CMD ["sh", "-c", "alembic upgrade head && exec uvicorn app.main:app --host 0.0.0.0 --port 8000"]
