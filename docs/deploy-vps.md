# Deploy backend lên VPS (Google Cloud Compute Engine)

File dùng: `docker-compose.yml` + `docker-compose.prod.yml` + `deploy/Caddyfile`.
Internet chỉ thấy cổng 80/443 (Caddy). Backend nghe thêm `127.0.0.1:8080` trên VPS; database, Redis và AI không mở cổng.

## 0. Trước khi bắt đầu (trên máy dev)
Code và ba file deploy phải có trên GitHub (commit + push `dev/tuan`). `git clone` không lấy được thay đổi chưa push.

## 1. Tạo VM
- e2-standard-2, region asia-southeast1, Debian 13, đĩa balanced **50 GB**.
- Networking: tick **Allow HTTP** và **Allow HTTPS**. Không thêm rule cho cổng nào khác.
- Sau khi tạo: VPC network > IP addresses > đổi IP ngoài thành **static**.

## 2. Tên miền
- Có tên miền: tạo bản ghi A, ví dụ `api.tenmien.com` → IP static.
- Chưa có: dùng `<IP đổi dấu chấm thành gạch>.sslip.io`, ví dụ IP `34.87.1.2` → `34-87-1-2.sslip.io`.

## 3. Cài Docker và lấy code (SSH vào VM)
```bash
sudo apt-get update && sudo apt-get install -y git
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
```
Thoát SSH rồi vào lại, sau đó:
```bash
docker compose version    # cần >= 2.24
git clone -b dev/tuan https://github.com/queoisme/FinMate_BE.git
```
Repo private thì đăng nhập GitHub bằng token hoặc deploy key.

## 4. Chép file không có trong git
Trên máy Windows, tại thư mục `FinMate_BE`:
```powershell
tar -czf "$HOME\finmate-files.tar.gz" backend/.env ai-service/.env ocr-service/.env stt-service/.env ai-service/models ocr-service/models stt-service/models
```
Gửi lên VM (cần Google Cloud CLI; hoặc dùng nút **Upload file** của cửa sổ SSH trên web):
```powershell
gcloud compute scp "$HOME\finmate-files.tar.gz" finmate-be:~ --zone=<zone-của-VM>
```
Trên VM:
```bash
cd ~/FinMate_BE && tar -xzf ~/finmate-files.tar.gz && chmod -R a+rX */models && chmod 600 */.env && rm ~/finmate-files.tar.gz
```
File nén chứa toàn bộ key: xóa luôn bản trên máy Windows sau khi gửi xong. Dùng Firebase thì chép thêm `backend/secrets/` và bỏ comment volume FCM trong `docker-compose.yml`.

## 5. Cấu hình
### 5.1 File `.env` ở gốc repo (tạo mới trên VM)
```bash
cd ~/FinMate_BE
printf 'DOMAIN=%s\nPOSTGRES_PASSWORD=%s\n' "34-87-1-2.sslip.io" "$(openssl rand -hex 24)" > .env
chmod 600 .env
```
Đổi `34-87-1-2.sslip.io` thành tên miền của bạn. Mật khẩu chỉ gồm chữ và số nên không làm hỏng chuỗi kết nối. Nó chỉ được áp dụng khi database tạo lần đầu.

### 5.2 Sinh bí mật mới cho production (không dùng lại giá trị dev)
```bash
openssl rand -hex 48      # JWT_SECRET
openssl rand -hex 32      # mỗi khóa nội bộ chạy một lần
openssl rand -base64 32   # SECRETS_MASTER_KEY, chạy hai lần: backend và ai-service dùng hai khóa khác nhau
```

Các khóa nội bộ phải khớp từng cặp:

| Giá trị | Nằm ở |
|---|---|
| Khóa A | `AI_SERVICE_API_KEY` (backend) = `INTERNAL_API_KEY` (ai-service) = `AI_SERVICE_API_KEY` (ocr-service) |
| Khóa B | `OCR_SERVICE_API_KEY` ở backend, ai-service và ocr-service |
| Khóa C | `STT_SERVICE_API_KEY` ở backend và stt-service |

### 5.3 Sửa `backend/.env` (`nano backend/.env`)
- **Xóa:** `DEV_SEED_OPS_ACCOUNTS`, `DEV_SEED_OPS_PASSWORD`, `DEV_SEED_DEMO_EMAIL`, `DEV_SEED_DEMO_PASSWORD`, `FORCE_REAL_EMAIL`, `AGENT_EVAL_ENABLED` nếu có (bật cờ này thì backend từ chối khởi động).
- **Đổi:** `ADMIN_SEED_EMAIL` thành email thật nhận được thư, `ADMIN_SEED_PASSWORD` mạnh, `HANGFIRE_DASHBOARD_PASS`.
- **Email bắt buộc phải chạy:** `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`. Ở Production, tài khoản quản trị đăng nhập cần mã gửi qua email (2FA), người dùng mới cần mã xác minh. Brevo chặn IP lạ: thêm IP static của VM tại https://app.brevo.com/security/authorised_ips.
- **Đăng nhập Google qua trình duyệt:** `GOOGLE_REDIRECT_URI=https://<DOMAIN>/api/v1/auth/google/callback`, và thêm đúng URL này trong Google Cloud Console > Credentials > OAuth client.
- **Web quản trị:** `OPS_WEB_ORIGINS=https://<domain-web-quản-trị>`. Để trống thì trình duyệt không gọi được API.
- `TRUSTED_PROXIES`, `DATABASE_URL`: compose đã ghi đè, không cần sửa.
- Backend từ chối khởi động nếu bí mật còn chữ `change-me`.

### 5.4 Google Vision
Nếu `GOOGLE_VISION_API_KEY` bị giới hạn theo IP, thêm IP static của VM.

## 6. Chạy
```bash
cd ~/FinMate_BE
echo 'export COMPOSE_FILE=docker-compose.yml:docker-compose.prod.yml' >> ~/.bashrc && source ~/.bashrc
docker compose config -q && docker compose up -d --build
```
Nhờ biến `COMPOSE_FILE`, các lệnh `docker compose` sau đó tự dùng cả hai file. **Đừng chạy chỉ file gốc trên VPS**: nó publish cổng database và chạy chế độ Development. Lần build đầu lâu vì phải tải thư viện và model PaddleOCR.

## 7. Kiểm tra
```bash
docker compose ps
curl -i http://127.0.0.1:8080/health/ready
docker compose logs --tail=100 backend
docker compose logs --tail=50 caddy
```
Từ máy khác mở `https://<DOMAIN>/health/ready`: phải trả 200 và có chứng chỉ hợp lệ. Sau đó thử đăng ký (nhận được email), đăng nhập, quét một hóa đơn, ghi một giao dịch bằng giọng nói, và xem `docker stats` lúc đang quét.

| Triệu chứng | Xem |
|---|---|
| Backend khởi động lại liên tục | `docker compose logs backend`: thường là bí mật còn `change-me`, `SECRETS_MASTER_KEY` sai định dạng, hoặc `JWT_SECRET` ngắn hơn 64 ký tự |
| Không có HTTPS | DNS đã trỏ đúng IP chưa, đã tick Allow HTTP/HTTPS chưa; `docker compose logs caddy` |
| Không nhận được email | Brevo chưa cho phép IP của VM, hoặc người gửi chưa xác minh |
| `password authentication failed` | Đổi `POSTGRES_PASSWORD` sau khi database đã được tạo |

Mở Hangfire: `gcloud compute ssh finmate-be -- -L 8080:127.0.0.1:8080`, rồi vào http://localhost:8080/hangfire.

## 8. Dữ liệu
Database trên VPS là **database mới**: migration và dữ liệu seed tự chạy, còn dữ liệu trên máy dev (người dùng, tài liệu tri thức, key nhập qua trang quản trị, cấu hình đã sửa) **không** đi theo. Nhập lại qua trang quản trị, hoặc dump/restore (khi đó phải giữ nguyên hai `SECRETS_MASTER_KEY` cũ).

Sao lưu (chạy trước mỗi lần cập nhật, rồi chép file ra ngoài VM):
```bash
mkdir -p ~/backups
docker compose exec -T postgres-main pg_dump -U finmate -Fc finmate_main > ~/backups/main-$(date +%F).dump
docker compose exec -T postgres-ai pg_dump -U finmate -Fc finmate_ai > ~/backups/ai-$(date +%F).dump
```
**Không bao giờ chạy `docker compose down -v`**: lệnh này xóa database.

## 9. Cập nhật phiên bản
```bash
cd ~/FinMate_BE && git pull && docker compose up -d --build && docker image prune -f
```

## Chưa có
Sao lưu tự động, CI/CD, và dịch vụ theo dõi bên ngoài cho `/health/ready` (backend không tự báo được khi chính nó sập).
