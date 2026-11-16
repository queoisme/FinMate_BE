# TASKS.md — FinMate Task Board (index)

> Cập nhật file phần tương ứng (trong `tasks/`) sau mỗi task hoàn thành — file này chỉ là mục lục,
> không chứa task.
> Format trong mỗi file con: `- [x] Task name — *Note ngắn nếu có*`

**Split 2026-11-16 (file gốc 1646 dòng/~347KB, quá to để đọc/sửa rẻ mỗi lần — xem rule "split a
long doc/memory file" trong `WORKING_RULES.md`).** Nội dung gốc chia theo Phase, không mất gì —
đọc đúng file ứng với Phase đang cần, đừng đọc hết cả 4 file cho 1 việc nhỏ.

## Legend

```
[ ] Todo       — chưa bắt đầu
[~] In Progress — đang làm
[x] Done       — hoàn thành
[!] Blocked    — bị block bởi task khác (ghi rõ blocked by gì)
[-] Skipped    — bỏ qua có lý do (ghi rõ lý do)
```

## Mục lục theo Phase

| File | Phase | Chủ đề |
|---|---|---|
| [`tasks/TASKS_P00-18.md`](tasks/TASKS_P00-18.md) | 0–18 | Setup, Auth/User, Financial Accounts, Categories, Notifications/Transactions core, Budget/Goals, Reports, Gamification (cũ), Admin, AI Service, đối chiếu docx Core Flow 1&2, FCM, role/xoá tài khoản, offline sync, nhánh 85%, hard-block production, OTP email, Google login |
| [`tasks/TASKS_P19-51.md`](tasks/TASKS_P19-51.md) | 19–51(b) | OCR hoá đơn, chat/Rasa→intent, tách phân loại bill, nối hoá đơn↔giao dịch, split theo món, vòng học, Gemini, ERD v5_1 (5 giai đoạn), voice, finance methods survey, OpenRouter fallback, admin config/secrets, QR chuyển khoản |
| [`tasks/TASKS_P52-76.md`](tasks/TASKS_P52-76.md) | 52–76 | Đối chiếu yêu cầu sau Phase 19–51, gỡ gamification, Dự báo tài chính (3 đợt), Nhóm (4 đợt), Vận hành nội bộ (5 đợt + fix), Giám sát hệ thống (3 đợt), Thông tin thị trường (4 đợt), Cộng đồng, Gói đăng ký SePay |
| [`tasks/TASKS_P77-85.md`](tasks/TASKS_P77-85.md) | 77–85 | Purchase-check, AI Financial Agent, bỏ Rasa/intent in-process, Hoàn tiền & đảo giao dịch, Bản trùng có liên kết, Ghép cặp chuyển tiền tự ví, Sức khoẻ listener & độ phủ dữ liệu, Quỹ chung của nhóm |

## Trạng thái nhanh (xem `CLAUDE.md` "Current state" để biết bản tóm tắt đầy đủ hơn, luôn cập nhật hơn số dưới đây)

424/450 tasks done tính tới 2026-11-02 (trước Phase 57+). Các Phase sau 2026-11-02 (Group finance,
Ops, Monitoring, Market info, Community, Subscriptions, Refunds, Duplicates, Transfers, Listener
health, Group funds) đã **done** theo ghi nhận trong từng file con — xem file con tương ứng để biết
chi tiết/trạng thái từng task, đừng chỉ tin con số 424/450 vì nó đã cũ hơn các Phase mới.
