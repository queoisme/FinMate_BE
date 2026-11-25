# Phase 91 — Xuất dữ liệu

> Trạng thái: **đã duyệt 2026-11-25**, Task 0–12 xong (backend 3946/3948, chỉ 2 đỏ có sẵn; ai-service 2255 passed). Còn kiểm tay S1 trên Excel Windows / Google Sheets. D1–D18 theo đề xuất (D17: chỉ CSV), kèm S1–S5 bên dưới (S thắng D và phần thân khi mâu thuẫn). Nhánh `feat/data-export` từ `main` @ `5aeec81` (đã có Phase 90, PR #35).
> Luật nghiệp vụ BR-EXP-01…14 do chủ sản phẩm chốt; kế hoạch này chỉ thêm cách làm, danh mục gói, chỗ lưu file và các điểm D1–D18.

## Đã duyệt (2026-11-25): điều chỉnh S1–S5

- **S1 (Excel vùng Việt Nam).** `GET /exports/transactions.csv` nhận thêm `delimiter=comma|semicolon` (mặc định `comma`; giá trị khác → 400). **Không** ghi dòng `sep=` (Excel bỏ qua BOM khi có dòng này, mất dấu tiếng Việt). `CsvWriter` nhận dấu phân cách làm tham số; ô được bọc ngoặc khi chứa **dấu phân cách đang dùng**, `"`, CR, LF hoặc khoảng trắng đầu/cuối (ô có `,` không cần bọc khi dùng `;` và ngược lại). Test cho cả hai. Gói đầy đủ luôn dùng `comma`. README trong gói ghi cách mở bằng Excel (Data → From Text/CSV, chọn UTF-8 65001 và dấu phân cách) và Google Sheets (File → Import). Báo cáo phase ghi kết quả **kiểm tay**: Excel trên Windows đặt vùng Vietnamese và Google Sheets, với cả hai dấu phân cách.
- **S2 (cột Trạng thái).** `TransactionStatus` chỉ có `Draft`/`Confirmed` (đã kiểm `FinMate.Domain/Enums/TransactionStatus.cs`); từ chối nháp là xoá mềm. Cột chỉ có "Đã xác nhận" / "Chờ duyệt"; bỏ "Đã từ chối". Tham số `status` = `Confirmed` | `Draft` | `All`.
- **S3 (tần suất CSV).** `dataExport.csvPerHour` (mặc định 20) theo user, cửa sổ trượt 60 phút đếm trong Redis (cùng kiểu cooldown OTP); vượt → 429 `EXPORT_RATE_LIMITED`. Chỉ đếm lần trả file thành công (422/404 không đốt lượt). Test.
- **S4 (`maxArchiveMb` = 25).** Ghi vào `docs/ops-runbook.md` và REQUIREMENTS §13.4: gói lưu trong Postgres nên có thể còn trong bản sao lưu DB lâu hơn 72 giờ (đã mã hoá bằng `SECRETS_MASTER_KEY`); hướng sau này là kho file riêng không nằm trong sao lưu.
- **S5 (xin xoá khi đang xuất).** `DELETE /auth/account` trả 409 `DATA_EXPORT_IN_PROGRESS` khi user có yêu cầu `Pending`/`Running` (kiểm sau bước xác thực mật khẩu, trước khi tạo yêu cầu xoá). Ghi §13.4: sau khi đã xin xoá, người dùng lấy dữ liệu qua hỗ trợ (DSAR đầy đủ, phase sau). "Báo nhóm Flutter": màn xoá tài khoản nhắc tải dữ liệu trước và xử lý mã lỗi mới. Test ở Task 9.
- **S6 (bổ sung sau review PR #36, 2026-11-26): CSV có tuỳ chọn gồm giao dịch đã xoá.** Mở rộng D6: `GET /exports/transactions.csv` thêm `includeDeleted` (mặc định `false`, giữ nguyên hành vi cũ). Bật thì dòng đã xoá/đã gộp nằm chung file, thêm cột cuối "Đã xoá lúc"; dòng đã xoá luôn tính 0 vào chi tiêu (`TransactionCsvFormat` dựa vào `DeletedAt`, không phụ thuộc loại file). Kịch bản (a) kiểm: xoá 2 khoản chi, mặc định 48 dòng, `includeDeleted=true` 50 dòng và tổng cột 17 vẫn bằng `totalSpentCents`.

## 1. Đã kiểm trong code (những gì dùng lại)

| Cần | Có sẵn | Ghi chú |
|---|---|---|
| OTP email | `IOtpService` (`Application/Auth/Otp/`), Redis, mã băm SHA-256, 10 phút, 5 lần sai, cooldown theo email; mục đích nằm trong khoá | Thêm `OtpPurpose.DataExport`. Gửi qua `IEmailSender` (Brevo, không có key thì ghi log mã). Mẫu luồng: `OpsTwoFactorService.SendCodeAsync` (gửi lỗi thì `ClearCooldownAsync`) |
| Mã hoá khi lưu | `IAttachmentProtector` → `SecretCipher` (AES-256-GCM, `SECRETS_MASTER_KEY`, AAD = `support-attachment:{id}`), ảnh ticket nằm ở cột `bytea` của `support_ticket_attachments` | Dùng **cùng `SecretCipher`** với AAD riêng `data-export:{id}`. Xem mục 3 |
| Job nền | Hangfire (`RecurringJob.AddOrUpdate` trong `Program.cs`, `IBackgroundJobClient` đã dùng ở `ForecastAlertJobs`) | |
| Push | `IPushNotificationService` → `FcmPushNotificationService` (chốt `PushEnabled` ở đây) | |
| Sự cố giám sát | `IIncidentService.RaiseAsync(IncidentSignal, …)`, một dòng active / fingerprint | Fingerprint không chứa user id |
| Quy tắc chi/thu | `NetFlow.Of` (hoàn trừ chi, cặp đảo bị loại), `LoanExclusion` (danh mục slug `loan`), `Transfer` không là chi/thu (Q16), `TransferLegs` | Cột "tính vào chi tiêu" của CSV gọi đúng các hàm này, không viết lại luật |
| Xoá cứng | `UserHardDeleter` xoá dòng `users`, mọi bảng con đi theo FK `ON DELETE CASCADE` | Bảng mới có FK cascade tới `users` là đủ, thêm test |
| Chờ xoá không đăng nhập được | `DataDeletionRequest` + `users.deleted_at` đặt ngay khi xin xoá | Giữ nguyên |
| Cờ tính năng | `FeatureSettings` trong `SystemConfigDocument` | Thêm `DataExport = true` |
| Câu trả lời mẫu | bảng `canned_replies` | Gieo một dòng bằng migration |
| Log request | `RequestLoggingMiddleware` chỉ ghi `Path`, **không** ghi query string | Token tải để ở query, không ở path |

Mốc test (Phase 90 Task 14, đo lại ở Task 0): backend 3819/3821, 2 đỏ có sẵn (`MethodSelectionOnSeedDataTests…GetsCashFlowBudget…`, `NotificationsControllerTests.Analyze_HighConfidence…AutoCommits…`); ai-service 2255 passed, 483 skipped.

## 2. Danh mục bảng có dữ liệu thuộc về một user

Đã đi qua toàn bộ 91 `DbSet` của `FinMateDbContext` và các bảng có `user_id_hash` ở AI DB. "Gói" = file trong ZIP. Cột "Bỏ" liệt kê cột **không** đưa ra; cột không nêu thì có. Mọi id nội bộ của người khác (nhân viên, thành viên nhóm) đều bị bỏ, kể cả khi không nêu.

### 2.1 Tài khoản, cài đặt, đồng ý

| Bảng | Liên kết | Gói | File | Bỏ | Vì sao |
|---|---|---|---|---|---|
| `users` | `id` | Có | `profile.json` | `password_hash`, `google_id`, `community_muted_until` giữ (là của mình), `is_locked` giữ | Mật khẩu băm là bí mật (BR-EXP-08). `google_id` là định danh bên thứ ba dùng để đăng nhập, coi như thông tin xác thực → bỏ, chỉ ghi `signInWithGoogle: true/false` |
| `users.notification_prefs` (jsonb) | | Có | `notification_settings.json` | | Cùng với ngưỡng số dư thấp, giờ yên lặng, `listenerHealthAlertsEnabled`… |
| Cột đồng ý trên `users` (`terms_*`, `ai_consent_*`, `training_consent_*`, `survey_ai_consent_*`, `community_consent_*`) | | Có | `consents.json` | | Xem **D3**: hiện chỉ lưu trạng thái mới nhất, không có lịch sử |
| `user_roles` | `user_id` | Có | `profile.json` (`role`) | `assigned_by_user_id` | Người gán là nhân viên |
| `refresh_tokens` | `user_id` | **Không** | | toàn bộ | Token (dù băm) là bí mật |
| `device_tokens` | `user_id` | Có | `devices.json` | `token` | Token FCM là năng lực gửi push (BR-EXP-08). Giữ nền tảng, ngày tạo, lần thấy cuối |
| `device_listener_statuses` | `user_id` | Có | `devices.json` (`listener`) | | Hãng máy, phiên bản app, quyền, package đã chọn: dữ liệu về máy của chính user |
| `device_listener_daily` | `user_id` | Có | `devices.json` (`listenerDays`) | | |
| `listener_upload_gap_daily` | không có user | Không | | | Số đếm gộp theo phiên bản app |
| `data_deletion_requests` | `user_id` | Có | `account_requests.json` | | |
| `personal_data_exports` (mới) | `user_id` | Có | `account_requests.json` | `download_token_hash` | Chính các lần xuất trước |
| `audit_logs` | `user_id` | Một phần | `security_log.json` | `metadata`, mọi sự kiện ngoài danh sách cho phép | Xem **D4** |
| `user_notifications` | `user_id` | Có | `notifications.json` | `related_entity_type/id` giữ | Hộp thư trong app |
| `forecast_alerts` | `user_id` | Có | `notifications.json` (`forecastAlerts`) | `dedup_key`, `data` | `dedup_key`, `data` là khoá/payload nội bộ để định tuyến; tiêu đề và nội dung giữ |

### 2.2 Ví, danh mục, giao dịch

| Bảng | Liên kết | Gói | File | Bỏ | Vì sao |
|---|---|---|---|---|---|
| `financial_accounts` | `user_id` | Có (cả đã xoá, kèm `deletedAt`) | `accounts.json` | `provider_config_id` thay bằng tên nhà cung cấp | |
| `categories` (`user_id` = user) | `user_id` | Có | `categories.json` | | Danh mục hệ thống không xuất, chỉ hiện tên ở nơi dùng |
| `user_hidden_categories` | `user_id` | Có | `categories.json` (`hidden`) | | |
| `merchant_category_overrides` | `user_id` | Có | `category_rules.json` | | Luật "nơi chi → danh mục" do user dạy |
| `counterparty_category_overrides` | `user_id` | Có | `category_rules.json` | | Số tài khoản người nhận là dữ liệu user tự lưu về giao dịch của mình (như tên nơi chi) |
| `transactions` | `user_id` | Có | `transactions.csv` (+ `transactions_deleted.csv`, **D6**) | `client_request_id`, `notification_log_id`, `balance_after_cents`, `provider_reference`, `exclude_from_forecast`/`forecast_exclusion_reason` giữ, `transfer_dismissed_at`, `support_ticket_id` | BR-EXP-05. `provider_reference` là mã tham chiếu ngân hàng lấy từ thông báo thô → bỏ cùng nhóm |
| `notification_logs` | `user_id` | Một phần | `bank_notifications.json` | `notification_title`, `notification_body`, `content_hash`, `error_message`, `retry_count` | Chỉ giữ: package, thời điểm nhận, trạng thái xử lý, id giao dịch tạo ra. Nội dung thô bị loại (BR-EXP-05/08) |
| `ai_results` | qua `notification_logs` | **Không** | | toàn bộ | Dữ liệu AI nội bộ (BR-EXP-05) |
| `receipt_scans` | `user_id` | Có | `receipts.json` | `ocr_result`, `ocr_engine`, `confidence`, `category_source` | `ocr_result` là chữ máy đọc thô từ ảnh, cùng loại với thông báo thô; tổng, nơi bán, ngày, danh mục giữ |
| `receipt_scan_items` | qua scan | Có | `receipts.json` (`items`) | `category_source` | |
| `receipt_scan_links` | qua scan | Có | `receipts.json` (`transactionIds`) | | |
| `transaction_anomalies` | `user_id` | Có | `anomalies.json` | `counterparty_key` | Khoá chuẩn hoá nội bộ |
| `transaction_merge_snapshots` | `user_id` | **Không** | | toàn bộ | Bản sao kỹ thuật để hoàn tác, chứa số dư sau giao dịch và id log thông báo (BR-EXP-05); nội dung đã có trong giao dịch còn lại; tự xoá sau `snapshots.retentionDays`. Ghi trong README |
| `qr_transfer_intents` | `user_id` | Có | `qr_transfers.json` | | Người nhận do user tự quét, là bản ghi giao dịch của user |
| `daily_summaries` | `user_id` | **Không** | | | Bảng đệm tính lại được từ giao dịch; số tổng của báo cáo xem trong app |

### 2.3 Kế hoạch tài chính, dự báo

| Bảng | Liên kết | Gói | File | Bỏ | Vì sao |
|---|---|---|---|---|---|
| `budgets` (`user_id` = user) | `user_id` | Có | `budgets.json` | | Ngân sách nhóm thuộc nhóm, không xuất |
| `budget_periods` | qua budget cá nhân | Có | `budgets.json` (`periods`) | `alert_over_sent_at` giữ | |
| `saving_goals` (`user_id` = user) | `user_id` | Có | `goals.json` | `goal_template_id`, `user_method_id` thay bằng mã | |
| `goal_contributions` | `user_id` | Có, **cả** khoản góp vào mục tiêu nhóm | `goals.json` (`contributions`) | | Khoản góp của chính user (BR-EXP-07) |
| `recurring_items` | `user_id` | Có | `recurring_items.json` | `counterparty_key` | |
| `loans` | `user_id` | Có | `loans.json` | `client_request_id` | Tên người vay/cho vay là sổ của chính user |
| `loan_repayments` | qua loan | Có | `loans.json` (`entries`) | `client_request_id` | |
| `user_methods` | `user_id` | Có | `finance_methods.json` | | Mã phương pháp + tham số |
| `method_recommendations` | `user_id` | Có | `finance_methods.json` (`recommendations`) | `prompt_version`, `method_versions` | Lý do, nguồn (AI/luật), kết quả giữ; `signals_snapshot` giữ (đã ẩn danh, suy ra từ câu trả lời của user) |
| `user_survey_answers` | `user_id` | Có | `finance_methods.json` (`surveyAnswers`) | | Kèm chữ câu hỏi và lựa chọn để đọc được |
| `spending_insights` | `user_id` | Có | `insights.json` | | |
| `forecast_snapshots` | `user_id` | Có | `forecasts.json` | `method`, `config_version` | Kết quả dự báo về chính user |
| `forecast_feedback` | `user_id` | Có | `forecasts.json` (`feedback`) | `cause`, `labeled_by_user_id`, `labeled_at`, `status` | Lý do và ghi chú user gửi giữ; nhãn là việc của nhân viên |

### 2.4 Nhóm

| Bảng | Liên kết | Gói | File | Bỏ | Vì sao |
|---|---|---|---|---|---|
| `groups` | qua `group_members` | Một phần | `groups.json` | `join_code`, mọi cài đặt nhóm, `avatar_url`, `created_by_user_id` | Tên, loại nhóm, ngày tạo để user nhận ra nhóm |
| `group_members` (của user) | `user_id` | Có | `groups.json` (`membership`) | `default_financial_account_id` giữ | Vai trò, quyền, tham gia/rời |
| `group_members` (người khác) | | Chỉ vai trò | `groups.json` (`members: [{ role, isYou }]`) | id, tên, email, ngày | BR-EXP-08 |
| `group_share_rules` | `user_id` | Có | `groups.json` (`shareRules`) | | |
| `transactions` có `group_id` của **user** | `user_id` | Có | đã nằm trong `transactions.csv`, cột "Nhóm" | | |
| `transactions` có `group_id` của **người khác** | | **Không** | | | Kịch bản (d) |
| `group_bills` / `group_bill_splits` | `paid_by_user_id`, `split.user_id` | Một phần | `groups.json` (`bills`) | Xem **D10** | |
| `group_settlements` | `from_user_id` / `to_user_id` | Có | `groups.json` (`settlements`) | id người kia (ghi "thành viên") | |
| `group_debt_reminders` | `debtor_user_id` / `sent_by_user_id` | Có | `groups.json` (`debtReminders`) | id người kia | |
| `group_fund_entries` (`member_user_id` = user) | | Có | `groups.json` (`fundEntries`) | `client_request_id`, `deleted_by_user_id` | Dòng quỹ của chính user (BR-EXP-07) |
| `group_funds` | qua dòng quỹ | Chỉ tên quỹ | | số dư quỹ | Số dư là của nhóm |
| `group_invitations` | `accepted_by_user_id`, `created_by_user_id`, `email` = email user | Một phần | `groups.json` (`invitations`) | `token_hash`, `email` người được mời, `message` của người khác | |
| `group_approvals` | `requested_by_user_id` / `decided_by_user_id` | Một phần | `groups.json` (`approvals`) | `payload_json`, `decision_reason` (người khác viết), id người kia | |
| `group_activities` (`actor_user_id` = user) | | Có | `groups.json` (`activity`) | | Loại, thời điểm |
| `group_categories`, `budgets`/`saving_goals` có `group_id`, `group_funds` số dư | nhóm | **Không** | | | Dữ liệu của nhóm; xuất dữ liệu nhóm là việc sau (§13.4) |

### 2.5 Hỗ trợ, cộng đồng, gói trả phí

| Bảng | Liên kết | Gói | File | Bỏ | Vì sao |
|---|---|---|---|---|---|
| `support_tickets` (`requester_user_id` = user) | | Có | `support.json` | `assignee_user_id`, `parent_ticket_id` giữ | |
| `support_ticket_messages` | qua ticket | Chỉ `Customer` và `Staff` | `support.json` (`messages`) | `InternalNote` (toàn bộ dòng), `author_user_id` (thay bằng "Bạn"/"Nhân viên") | BR-EXP-08 |
| `support_ticket_attachments` | qua ticket | Chỉ số lượng ảnh mỗi tin | | `data`, `nonce` | Không ảnh (BR-EXP-08) |
| `support_access_grants` | qua ticket | Có | `support.json` (`accessRequests`) | `requested_by_user_id` | Phạm vi, lý do, trạng thái: user đã thấy và duyệt trong app |
| `support_data_access_logs` (`customer_user_id` = user) | | Có | `support.json` (`accessLog`) | `actor_user_id` | Cùng nội dung `GET /support/access-log` |
| `ops_approval_requests` (`target_id` = user) | | **Không** | | | Hồ sơ duyệt nội bộ, lý do do nhân viên viết. Xem **D12** |
| `community_posts` (`author_id` = user) | | Có | `community.json` | | Bài của chính user, kể cả ẩn danh |
| `community_comments` (`author_id` = user) | | Có | `community.json` | | |
| `community_post_images` | qua post | Không | | `data` | Không ảnh; ghi số lượng |
| `community_post_votes` (`user_id`) | | Có | `community.json` (`votes`: id bài, thời điểm) | | |
| `community_reports` (`reporter_id`) | | Có | `community.json` (`reports`) | `resolved_by` | Lý do, trạng thái, id nội dung bị báo |
| Cột cộng đồng trên `users` (tỉnh, khu vực, đồng ý, `community_muted_until`) | | Có | `profile.json` | | |
| `subscription_orders` (`user_id`) | | Có | `subscriptions.json` | `features_json` thu gọn thành danh sách tính năng, `review_reason`, `review_alerted_at`, `next_check_at` | Lý do xem xét là ghi chú nội bộ |
| `user_subscription_periods` | `user_id` | Có | `subscriptions.json` (`periods`) | | |
| `subscription_usage` | `user_id` | Có | `subscriptions.json` (`usage`) | | |

### 2.6 Không thuộc về user (không đưa vào gói)

Danh mục/cấu hình chung: `provider_configs`, `vietqr_banks`, `finance_methods`, `goal_templates`, `method_goal_templates`, `survey_questions`, `survey_options`, `system_config_versions`, `legal_versions`, `roles`, `role_permissions`, `secret_store`, `economic_assumptions`, `subscription_plans`, `subscription_plan_features`, `canned_replies`, `community_places`, `market_sources`, `market_documents`, `market_signals`, `market_events`, `fuel_prices`, `ops_incidents`, `ops_service_status`, `ops_error_events`. Một số có `created_by`/`updated_by`/`reviewed_by` của nhân viên: đó là dấu vết công việc nội bộ, không phải dữ liệu cá nhân của khách → không xuất, kể cả khi người xin là nhân viên. `group_*` của nhóm như mục 2.4.

### 2.7 Ngoài DB backend

| Nơi | Gói | Vì sao |
|---|---|---|
| AI DB `agent_user_memory` | **Có**, `agent_memory.json`, lấy qua `IAgentAiClient.GetMemoryAsync` | BR-EXP-07 đòi. User đã xem/sửa được qua API; xem **D2** về mâu thuẫn với BR-EXP-08 |
| AI DB còn lại theo `user_id_hash`: `agent_runs`, `user_merchant_memory`, `user_item_memory`, `ml_user_feedback`, `ml_item_feedback`, `ml_raw_samples`, `ml_training_samples`, `bill_unknown_scans`, `ops_analyze_log`, `ops_usage_daily` | Không | BR-EXP-08; README nói rõ: chỉ có mã băm, không truy ngược; mẫu huấn luyện bị xoá khi xoá tài khoản (NFR-SEC-25) |
| Redis: ngữ cảnh hội thoại ngắn hạn của agent, cache hồ sơ, OTP | Không | Tạm thời, tự hết hạn |
| `ocr-service`, `stt-service` | Không | Không lưu gì |

## 3. Chỗ lưu file (đề xuất)

Dùng **cơ chế của ảnh ticket**: bytes trong Postgres (`bytea`), mã bằng `SecretCipher` (AES-256-GCM, `SECRETS_MASTER_KEY`), AAD gắn với id yêu cầu (`data-export:{id:N}`) nên chép bản mã sang dòng khác không giải mã được. Thêm `IExportArchiveProtector` (cùng dạng `IAttachmentProtector`) để không tái dùng ngữ cảnh của ảnh.

- **Bảng tách riêng** `personal_data_export_archives (export_id PK/FK cascade, data bytea, nonce bytea, size_bytes)`, để đọc trạng thái/danh sách không kéo theo vài chục MB.
- Dựng ZIP trong `MemoryStream` có đếm byte; vượt `maxArchiveMb` thì dừng ngay, không đợi xong. Mã hoá một lần (GCM một khối); 25 MB ⇒ khoảng 50 MB RAM tạm thời, một job một lúc (Hangfire queue riêng `data-export`, 1 worker), chấp nhận được.
- Không dùng đĩa: container chạy non-root, `/app` không ghi được, nhiều instance không chung đĩa; không có blob storage trong stack.
- Ước lượng: 50.000 giao dịch × ~300 byte ≈ 15 MB CSV, nén còn vài MB. `maxArchiveMb` mặc định **25** (S4).
- Gói nằm trong Postgres nên có thể còn trong bản sao lưu DB lâu hơn 72 giờ (đã mã hoá); ghi runbook và §13.4 (S4).
- Xoá: job dọn dẹp xoá dòng `archives` khi hết hạn; xoá cứng user → cascade.

Phương án khác đã cân nhắc: Redis (mất khi restart, giới hạn bộ nhớ), đĩa tạm (không chia sẻ giữa instance), blob storage (dependency mới, cần duyệt).

## 4. Thiết kế

### 4.1 Cấu hình (`SystemConfigDocument`, nhóm `dataExport`)

`features.dataExport = true`; `dataExport.maxRangeDays = 366`, `maxRows = 50000`, `maxRequestsPerDay = 1`, `csvPerHour = 20` (S3), `fileRetentionHours = 72`, `maxArchiveMb = 25` (S4), `downloadLinkMinutes = 10`, `requestHistoryDays = 365`. Có validator như các nhóm khác.

### 4.2 Bảng mới (migration `AddPersonalDataExports`)

`personal_data_exports`: `id uuid`, `user_id` FK cascade, `status` (`pending|running|ready|failed|expired`), `format_version int`, `requested_at`, `started_at`, `ready_at`, `failed_at`, `failure_code`, `expires_at`, `expired_at`, `size_bytes bigint`, `attempts int`, `lease_until`, `download_token_hash char(64)`, `download_token_expires_at`, `download_count int`, `last_downloaded_at`, `counts_toward_limit bool`, `xmin`.
- `UNIQUE (user_id) WHERE status IN ('pending','running')` → chặn hai yêu cầu đang chạy kể cả khi gọi song song.
- Index `(user_id, requested_at desc)`, `(status, expires_at)`.

`personal_data_export_archives` như mục 3. `SupportTicketCategory.PersonalDataRequest` (lưu dạng chuỗi hay số: kiểm `SupportConfigurations` ở Task 1).

### 4.3 API (`ExportsController`, `[Authorize]`, `[RequireFeature(dataExport)]` trừ list/get)

| Endpoint | Trả |
|---|---|
| `GET /api/v1/exports/transactions.csv?from=&to=&accountId=&categoryId=&status=&delimiter=` | `text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="finmate-giao-dich-{from}-{to}.csv"`. `from`/`to` là ngày VN (`yyyy-MM-dd`, gồm cả hai đầu), mặc định tháng hiện tại; `status` = `Confirmed` (mặc định) \| `Draft` \| `All`; `delimiter` = `comma` (mặc định) \| `semicolon` (S1). Vượt khoảng/số dòng → 422 `EXPORT_RANGE_TOO_LARGE` (đếm trước, không dựng file); quá `csvPerHour` → 429 `EXPORT_RATE_LIMITED` (S3). `accountId`/`categoryId` của người khác → 404 |
| `POST /api/v1/exports/personal-data/otp` | 202. Kiểm hạn mức **trước** (vượt → 429, không gửi mail). Cooldown OTP → vẫn 202 (mã cũ còn dùng được), như `OpsTwoFactorService` |
| `POST /api/v1/exports/personal-data` `{ code }` | 202 + DTO. Thứ tự: cờ → hạn mức (429 `EXPORT_RATE_LIMITED`) → OTP (sai/thiếu/quá số lần → 403 `EXPORT_OTP_INVALID` / `EXPORT_OTP_TOO_MANY_ATTEMPTS`) → lưu `Pending` → enqueue. Kiểm hạn mức trước để không đốt mã. Thiếu `SECRETS_MASTER_KEY` → 422 `DATA_EXPORT_UNAVAILABLE` (**D8**) |
| `GET /api/v1/exports/personal-data` | Danh sách yêu cầu của user (mới nhất trước, cursor) kèm `status`, `requestedAt`, `readyAt`, `failedAt`, `failureReason` (câu tiếng Việt cố định), `expiresAt`, `expiredAt`, `downloadCount`, `lastDownloadedAt`, `sizeBytes` |
| `GET /api/v1/exports/personal-data/{id}` | Một yêu cầu. Id người khác/không có → 404 |
| `POST /api/v1/exports/personal-data/{id}/download-link` | Chỉ khi `Ready` (khác → 409 `EXPORT_NOT_READY`, `Expired` → 410 `EXPORT_EXPIRED`). Trả `{ url, expiresAt }`; url = `/api/v1/exports/personal-data/{id}/download?token=…` (32 byte ngẫu nhiên, base64url; DB giữ SHA-256). Link mới thay link cũ |
| `GET /api/v1/exports/personal-data/{id}/download?token=` | `[Authorize]`, cùng user (**D7**). Đổi token bằng một `UPDATE … WHERE id AND user_id AND token_hash AND token_expires_at > now() AND status = 'ready'` đặt `token_hash = NULL`; 0 dòng → 410 `EXPORT_LINK_EXPIRED` (id của người khác → 404). Rồi giải mã và trả `application/zip`. Link chết ngay khi bắt đầu tải (**D9**) |

DTO trường mới đều là endpoint mới; không đổi DTO cũ.

### 4.4 CSV giao dịch (BR-EXP-02…05)

`CsvWriter` thuần (Application, không dependency): UTF-8 có BOM, dấu phân cách là tham số (`,` hoặc `;`, S1, không có dòng `sep=`), CRLF, bọc `"` khi ô có dấu phân cách đang dùng, `"`, CR, LF hoặc khoảng trắng đầu/cuối, `"` nhân đôi; ô bắt đầu bằng `=` `+` `-` `@` `\t` `\r` thêm tiền tố `'` **trước** khi bọc ngoặc (vậy `=HYPERLINK("x")` → `"'=HYPERLINK(""x"")"`, mở ra thấy `'=HYPERLINK("x")`). Chỉ áp chống injection cho ô chữ; số tiền là số nguyên nên **không** bao giờ có `'`, kể cả số âm của cột "Số tiền tính vào chi tiêu" (**D5**). Ghi theo luồng ra response (không giữ cả file trong RAM ở CSV đồng bộ).

Cột (thứ tự cố định, tên tiếng Việt):

| # | Cột | Nguồn |
|---|---|---|
| 1 | Ngày | `yyyy-MM-dd` giờ VN |
| 2 | Thời điểm | ISO 8601 `yyyy-MM-ddTHH:mm:ss+07:00` |
| 3 | Loại | Chi / Thu / Chuyển ví |
| 4 | Số tiền | `amount_cents` (là đồng), luôn dương |
| 5 | Ví | tên ví |
| 6 | Ví đích | tên ví đích (chuyển ví); trống với giao dịch quỹ nhóm (không có ví đích) |
| 7 | Danh mục | tên (hệ thống, riêng, hoặc danh mục nhóm) |
| 8 | Nơi chi | `merchant_name` |
| 9 | Ghi chú | `description` |
| 10 | Nguồn | Tay / Thông báo / Giọng nói / Hoá đơn / Hỗ trợ (**D1**) |
| 11 | Trạng thái | Đã xác nhận / Chờ duyệt (S2) |
| 12 | Quan hệ | một giá trị theo thứ tự ưu tiên: Đảo giao dịch, Đã bị đảo, Hoàn tiền, Vay nợ, Lãi vay, Quỹ nhóm, Nghi trùng; trống nếu không (**D11**) |
| 13 | Liên quan đến | id tương ứng: giao dịch gốc (hoàn/đảo), giao dịch đảo (đã bị đảo), khoản vay, dòng quỹ, giao dịch nghi trùng |
| 14 | Gộp từ | `merged_sources` (vd. "Thông báo; Tay") |
| 15 | Nhóm | tên nhóm nếu user đã chia sẻ giao dịch này |
| 16 | Tính vào chi tiêu | Có / Không |
| 17 | Số tiền tính vào chi tiêu | có dấu: khoản chi được tính → `+số tiền`; hoàn tiền → `−số tiền`; còn lại 0 (**D5**) |
| 18 | Mã giao dịch | `id` |

"Tính vào chi tiêu" lấy từ `NetFlow.Of(...).SpentCents != 0` cộng loại `loan` (`LoanExclusion.IsLoan`) và `Transfer`, chỉ với `Confirmed`. Không có: nội dung thông báo, `clientRequestId`, `notificationLogId`, số dư sau, mã tham chiếu ngân hàng, độ tin cậy, phiên bản mô hình.

### 4.5 Job dựng gói

- `PersonalDataExportJob.RunAsync(exportId)`: lấy lease (`pending → running`, `lease_until = now + 15 phút`, `attempts++`) bằng update có điều kiện; user đã bị xoá mềm → `failed/account_deleted`, không thông báo.
- `IPersonalDataReader` (Infrastructure, một phương thức mỗi nhóm, mọi truy vấn lọc `userId`, `AsNoTracking`, có `IgnoreQueryFilters` chỉ ở nơi cố ý lấy cả bản đã xoá) → DTO xuất (`Application/DataExport/Package/`), **không** serialize entity (để cột mới thêm sau không lọt vào gói).
- `PersonalDataArchiveBuilder` (thuần): README + các file mục 2 + `transactions.csv` (dùng chung `CsvWriter` và mapper của 4.4, mọi trạng thái trừ đã xoá) + `transactions_deleted.csv`. JSON: `{ "formatVersion": 1, "exportedAt": "…+07:00", "items": [...] }`, tên trường camelCase như API (**D13**).
- Xong: lưu archive + `ready`, `expires_at = ready_at + fileRetentionHours`, một lượt lưu; rồi push (`action = data_export_ready`, `exportId`) và email "Dữ liệu của bạn đã sẵn sàng" (không link, không số liệu). Email gửi kể cả khi tắt push (giao dịch do user yêu cầu).
- Vượt `maxArchiveMb` → `failed/archive_too_large`, `RaiseAsync` fingerprint `data_export:archive_too_large`, mức Normal, tiêu đề không có user; push "chưa tạo được, đội ngũ đang xử lý".
- Lỗi khác: Hangfire thử lại 3 lần; hết lượt → `failed/internal_error`. Lỗi hệ thống **không** tính vào hạn mức (`counts_toward_limit = false`) (**D14**).
- `PersonalDataExportSweepJob` (5 phút): `pending` > 2 phút chưa chạy (enqueue bị mất) hoặc `running` quá lease → enqueue lại; quá 3 lần → `failed`.
- `PersonalDataExportCleanupJob` (mỗi giờ): `ready` quá `expires_at` → `expired`, xoá archive, huỷ token, audit; xoá dòng yêu cầu quá `requestHistoryDays`.
- Cờ tắt: không nhận yêu cầu/CSV/link mới; job không chạy (yêu cầu nằm `Pending`, chạy tiếp khi bật); dọn dẹp vẫn chạy (**D15**).

### 4.6 Audit (BR-EXP-11)

`DataExport.Requested`, `.Ready`, `.Failed`, `.LinkIssued`, `.Downloaded`, `.Expired`, `.TransactionsCsv` (**D16**). Metadata chỉ `exportId` (và `failureCode`). Không số tiền, không nội dung, không token, không bộ lọc.

### 4.7 Nhân viên (BR-EXP-14)

`SupportTicketCategory.PersonalDataRequest` (ưu tiên mặc định P2, vì có hạn luật 10 ngày), câu trả lời mẫu "Hướng dẫn tự tải dữ liệu" gieo bằng migration. Không endpoint `/ops` nào chạm tới xuất dữ liệu; test kiến trúc khoá: không controller `Ops*`/`Admin*` nào phụ thuộc vào dịch vụ xuất.

## 5. Cần quyết (mỗi mục có đề xuất)

- **D1. Nguồn "chat" không phân biệt được.** Giao dịch chat đã lưu mang `source = Manual` (`ChatCommandHandler`; `ARCHITECTURE.md`: `Chat` chỉ có ở nháp). *Đề xuất:* cột Nguồn ghi "Tay" cho cả gõ form và chat, thêm "Hỗ trợ" cho `Support`; ghi trong README. Muốn tách chat phải thêm giá trị enum và sửa đường chat: việc riêng, không làm ở phase này.
- **D2. Bộ nhớ Agent nằm ở AI DB**, mâu thuẫn chữ với BR-EXP-08 ("không dữ liệu AI DB"). *Đề xuất:* đưa vào, lấy qua API sẵn có (user vốn xem được); BR-EXP-08 hiểu là "không đọc thẳng AI DB và không đưa dữ liệu chỉ có mã băm". ai-service không trả lời sau 3 lần thử → gói vẫn xong, `agent_memory.json` = `{ "unavailable": true }` và README ghi lý do; không chặn cả quyền vì một dịch vụ phụ.
- **D3. Không có lịch sử đồng ý.** Chỉ có phiên bản + thời điểm **mới nhất** mỗi loại trên `users`; rút đồng ý ghi đè. *Đề xuất:* `consents.json` xuất trạng thái hiện tại từng loại (kể cả "đã từ chối AI" lưu là 0); từ phase này ghi audit `User.ConsentChanged` (metadata: loại, phiên bản, đồng ý/rút) ở `POST /users/me/consents` và đường rút, rồi đưa các dòng đó vào `consents.json` (`history`). README nói lịch sử chỉ có từ ngày 2026-11-25. (Liên quan NFR-SEC-29, không giải quyết ở đây.)
- **D4. `audit_logs` của user.** `user_id` là người thực hiện; với nhân viên, metadata có thể chứa id khách. *Đề xuất:* chỉ xuất sự kiện trong danh sách cho phép (đăng nhập/đăng xuất/đổi mật khẩu/đặt lại/OTP/đồng ý/xin xoá/xuất dữ liệu), chỉ `eventType` (kèm tên tiếng Việt), thời điểm và IP; không `metadata`.
- **D5. Kịch bản (a) cần cột có dấu.** "Tổng cột số tiền của dòng tính vào chi tiêu = `totalSpentCents`" chỉ đúng khi hoàn tiền trừ đi (Phase 81). *Đề xuất:* cột 4 "Số tiền" luôn dương; thêm cột 17 "Số tiền tính vào chi tiêu" có dấu, và test (a) cộng cột 17. Còn một chỗ lệch có chủ ý: tháng có chi ròng âm bị chặn ở 0 (`refundSurplusCents`), ghi trong README và tài liệu.
- **D6. Giao dịch đã xoá mềm.** Vẫn nằm trong DB tới khi xoá cứng, nên là dữ liệu đang giữ. *Đề xuất:* CSV hằng ngày không có; gói có `transactions_deleted.csv` cùng định dạng + cột "Đã xoá lúc". Bản bị gộp (`merged_into_transaction_id`) cũng vào đây, "Liên quan đến" = bản giữ lại. Cùng luật cho JSON: bản đã xoá có `deletedAt`.
- **D7. Link tải có đòi đăng nhập không.** "Gắn với user" ⇒ *Đề xuất:* có, `[Authorize]` + token một lần; app tải bằng HTTP client có bearer. Hệ quả: không giao URL cho trình duyệt hay `DownloadManager` của Android được. Phương án khác: chỉ token (link mở được ở mọi nơi trong 10 phút), giống handoff code Phase 18; không đề xuất vì link rò = cả gói dữ liệu.
- **D8. Thiếu `SECRETS_MASTER_KEY`.** *Đề xuất:* không lưu bản rõ; gói đầy đủ trả 422 `DATA_EXPORT_UNAVAILABLE`, CSV vẫn chạy; `app-config` báo `dataExport.packageAvailable=false` để app ẩn nút. Runbook: production phải có khoá (đã cần cho ảnh ticket).
- **D9. "Tải xong" thì link chết.** Server không biết chắc client nhận đủ. *Đề xuất:* link chết ngay khi bắt đầu trả file; tải đứt thì lấy link mới (còn trong 72 giờ, không giới hạn số link, mỗi lần đều audit). `downloadCount` đếm lần bắt đầu tải.
- **D10. Hoá đơn chia nhóm.** Tiêu đề hoá đơn do người khác trả là dữ liệu người khác viết. *Đề xuất:* hoá đơn user trả hoặc tạo: tiêu đề, số tiền, ngày, cách chia, phần của user; hoá đơn người khác trả mà user có phần: chỉ ngày, phần của user, danh mục, không tiêu đề, không ghi chú.
- **D11. Nhiều quan hệ trên một giao dịch** (vd. khoản vay đang nghi trùng). *Đề xuất:* một cột "Quan hệ" theo thứ tự ưu tiên như 4.4 để lọc được trong Excel; thông tin đầy đủ đã có ở cột Liên quan đến / Gộp từ.
- **D12. Hồ sơ duyệt nội bộ về user** (`ops_approval_requests`: mở khoá, huỷ xoá). *Đề xuất:* không xuất (lý do do nhân viên viết, là ghi chú nội bộ theo BR-EXP-08); kết quả đã thể hiện ở trạng thái tài khoản và `account_requests.json`. Đưa vào §13.4 cùng DSAR đầy đủ.
- **D13. Dạng JSON.** *Đề xuất:* tên trường camelCase tiếng Anh như API (máy đọc được, khớp tài liệu API); README có bảng giải nghĩa tiếng Việt từng file. Tiền là số nguyên đồng; trường giữ tên `…Cents` như API, README ghi "Cents = đồng". Thời điểm ISO 8601 `+07:00`, ngày `yyyy-MM-dd`.
- **D14. Hạn mức và yêu cầu thất bại.** *Đề xuất:* 24 giờ đếm theo `requested_at` của các yêu cầu `counts_toward_limit`; thất bại do hệ thống (`internal_error`, `archive_too_large`, `account_deleted`) không tính, để user thử lại được. `Expired` và `Ready` vẫn tính (kịch bản e).
- **D15. Cờ tắt và hạn 10 ngày.** *Đề xuất:* như 4.5; runbook ghi rõ: tắt quá 7 ngày khi còn yêu cầu `Pending` là có nguy cơ trễ hạn luật; giám sát mở sự cố Normal `data_export:overdue` khi có yêu cầu `Pending` > 5 ngày.
- **D16. Có ghi audit cho CSV không.** *Đề xuất:* có, `DataExport.TransactionsCsv`, không lưu bộ lọc hay số dòng; không hiện trong danh sách gói (đó là danh sách yêu cầu gói).
- **D17. `.xlsx`.** Các lựa chọn: ClosedXML (MIT, kéo `DocumentFormat.OpenXml` MIT, ~6–8 MB DLL, giữ cả sổ trong RAM); MiniExcel (Apache-2.0, ~0,5 MB, ghi theo luồng); EPPlus 5+ (Polyform Noncommercial: **không** dùng được). *Đề xuất:* **chỉ CSV** ở phase này. CSV có BOM mở thẳng bằng Excel và Google Sheets, đủ cho cả hai nhu cầu; thêm dependency phải duyệt TECH_STACK. Nếu sau này cần (định dạng số, nhiều sheet) thì chọn MiniExcel. Ghi §13.4.
- **D18. Thư viện đọc CSV trong test** ("mở bằng thư viện đọc CSV", kịch bản a). *Đề xuất:* `Microsoft.VisualBasic.FileIO.TextFieldParser` có sẵn trong .NET, không thêm package; kiểm thêm việc nó đọc đúng ô có xuống dòng và ngoặc kép.

## 6. Các bước (TDD, một commit mỗi task)

0. **Mốc.** Đo lại toàn bộ test backend + ai-service trên `feat/data-export` trước khi sửa; ghi tên test đỏ vào mục 8.
1. **Nền:** `OtpPurpose.DataExport`, cấu hình `dataExport` + cờ + validator, mã lỗi, `AuditEvents`, enum trạng thái, entity + configuration + migration `AddPersonalDataExports` (2 bảng, index, unique một phần), `SupportTicketCategory.PersonalDataRequest`, câu trả lời mẫu, `IExportArchiveProtector`. Test: cấu hình mặc định, migration Up/Down, protector (đúng id giải mã được, sai id thì null).
2. **`CsvWriter`** (unit, đỏ trước): BOM, không có dòng `sep=`, CRLF, cả hai dấu phân cách (ô có `,` bọc khi dùng `,`, không bọc khi dùng `;`, và ngược lại), ngoặc kép, xuống dòng trong ô, khoảng trắng đầu, injection cho từng ký tự `= + - @ \t \r`, số âm không bị thêm `'`, ô rỗng/null.
3. **Mapper giao dịch → dòng CSV** (unit): loại, nguồn, trạng thái, quan hệ theo ưu tiên, "tính vào chi tiêu" và số có dấu so với `NetFlow`/`LoanExclusion`/`Transfer` (bảng trường hợp: chi, thu, hoàn, đảo + bị đảo, vay, lãi vay, chuyển ví, quỹ nhóm, nháp), múi giờ quanh 00:00 VN.
4. **`GET /exports/transactions.csv`** (integration): mặc định `Confirmed` và `comma`, `delimiter=semicolon`, `delimiter` lạ → 400, quá `csvPerHour` → 429 (S3), lọc ví/danh mục/trạng thái, ví người khác → 404, khoảng > 366 ngày và > `maxRows` → 422, cờ tắt → 422, không gói trả phí nào chặn (BR-EXP-13), không log nội dung. Kịch bản **(a)** và **(b)**.
5. **Yêu cầu gói:** `otp`, `POST`, `GET` list/one (integration): thiếu/sai OTP → 403, mã của mục đích khác (VerifyEmail) → 403, đang chạy → 429, đã có trong 24 giờ → 429, thất bại hệ thống không tính, hai `POST` song song → một 202 một 429, id người khác → 404, audit `Requested`.
6. **`IPersonalDataReader` + `PersonalDataArchiveBuilder` + README** (integration trên Postgres thật): đủ mọi file mục 2; đọc lại ZIP; quét toàn bộ byte không có: mật khẩu băm, chuỗi refresh token, token FCM, OTP, nội dung thông báo thô, `content_hash`, email/tên/id của thành viên khác, ghi chú nội bộ, tiêu đề hoá đơn người khác trả. Kịch bản **(d)**.
7. **Job, lưu, thông báo:** lease, thử lại, sweep, vượt `maxArchiveMb` → `failed` + sự cố Normal, user bị xoá mềm giữa chừng, push/email đúng chữ (không số, không link), ai-service lỗi → `agent_memory.json` đánh dấu (D2).
8. **Link tải:** chỉ khi `Ready`, dùng một lần, hết `downloadLinkMinutes` → 410, link mới vô hiệu link cũ, user khác → 404, audit `LinkIssued`/`Downloaded`. Kịch bản **(c)**.
9. **Dọn dẹp và xoá:** `Expired` sau `fileRetentionHours`, archive bị xoá, link chết; `DELETE /auth/account` khi có yêu cầu `Pending`/`Running` → 409 `DATA_EXPORT_IN_PROGRESS`, không tạo yêu cầu xoá (S5); xoá cứng user xoá yêu cầu + archive (`UserHardDeleter`/`DataDeletionJob`); tài khoản chờ xoá không đăng nhập được (giữ test cũ). Kịch bản **(e)**.
10. **Nhân viên:** loại ticket mới tạo được từ app, câu trả lời mẫu có trong `/ops/canned-replies`, test kiến trúc không controller ops/admin nào dùng dịch vụ xuất. (Nếu D3 duyệt: audit đồng ý ở task này.)
11. **Tài liệu:** `docs/data-export-api.md` (mới), `docs/REQUIREMENTS.md` (FR-EXP-01…, §13.1, §13.4: DSAR đầy đủ (xem/sửa/xoá/rút đồng ý, hạn 2/10/20 ngày) gồm lấy dữ liệu sau khi đã xin xoá (S5), `.xlsx`, xuất dữ liệu nhóm, D12, gói trong bản sao lưu DB (S4)), `docs/ERD.md`, `docs/ops-api.md` + `docs/ops-runbook.md` (loại ticket, sự cố mới, D15, sao lưu S4), `.context/ARCHITECTURE.md` (mục mới, cần duyệt theo AGENTS.md §4), `.context/TASKS.md` Phase 91 kèm **"Báo nhóm Flutter"** và nhắc người viết Điều khoản / Chính sách bổ sung quyền tải dữ liệu, `CLAUDE.md` đoạn Phase 91.
12. **Toàn bộ test** backend + ai-service; không thêm test đỏ so với mốc Task 0; build không cảnh báo. Kiểm tay S1 (Excel Windows vùng Vietnamese + Google Sheets, cả hai dấu phân cách) ghi vào báo cáo; nếu không có máy Windows thì ghi rõ là chưa kiểm và nhờ người có máy.

Thứ tự: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 (2–3 làm trước 5–6 được; 4 và 5 độc lập).

## 7. Kịch bản đầu-cuối

- **(a)** 3 ví, 50 giao dịch tháng này (có hoàn tiền cùng tháng, cặp đảo, chuyển ví, cho vay + trả nợ, một khoản lãi vay); `GET transactions.csv` → `TextFieldParser` đọc đúng 50 dòng; tổng cột 17 = `totalSpentCents` của `GET /reports/monthly-summary` cùng tháng.
- **(b)** Ghi chú `=HYPERLINK("x")` → ô đọc ra là `'=HYPERLINK("x")`.
- **(c)** `POST personal-data` không mã → 403; xin mã, `POST` có mã → `Pending`; chạy job → `Ready`; `download-link` → tải được ZIP, mở được, có `README.txt`; gọi lại cùng link → 410.
- **(d)** A và B cùng nhóm; B chia sẻ giao dịch (nơi chi, ghi chú riêng) vào nhóm; gói của A không chứa id/nơi chi/ghi chú giao dịch của B, tên hay email của B.
- **(e)** Yêu cầu thứ hai trong 24 giờ → 429; đẩy đồng hồ qua `fileRetentionHours`, chạy dọn dẹp → `Expired`, không còn dòng archive, `download-link` → 410.

## 8. Mốc test

Đo 2026-11-25 trên `feat/data-export` @ `5aeec81` (chưa sửa code), podman + `mcr.microsoft.com/dotnet/sdk:9.0`, `--pids-limit=-1`:
- backend: **3820/3822** (24 phút), không cảnh báo CS. Hai test đỏ có sẵn trên `main`, cùng hai test ở mốc Phase 90:
  - `NotificationsControllerTests.Analyze_HighConfidenceFinancialNotification_AutoCommitsAndUpdatesBalance`;
  - `MethodSelectionOnSeedDataTests.TheExampleProfileInTheMdFile_GetsCashFlowBudget_WithASmallEmergencyBufferAsSupport`.
- ai-service: **2255 passed, 483 skipped** (không có Postgres).

Luật: không thêm test đỏ so với mốc này.

Kết quả Task 12 (2026-11-25): backend **3946/3948**, đúng hai test đỏ trên; không cảnh báo CS. ai-service 2255 passed, 483 skipped.

## 9. Báo nhóm Flutter (bản nháp, chép vào TASKS.md khi xong)

Màn "Dữ liệu của tôi" trong Cài đặt: (1) "Xuất giao dịch (CSV)": chọn khoảng ngày (≤ 366 ngày), ví, danh mục, trạng thái, dấu phân cách (gợi ý `semicolon` khi máy đặt vùng Việt Nam, vì Excel vùng VN tách cột bằng `;`) → tải `transactions.csv` rồi mở chia sẻ; (2) "Tải toàn bộ dữ liệu": giải thích (gồm gì, không gồm gì, file giữ 72 giờ), nút xin mã → nhập mã email → trạng thái `Pending/Running/Ready/Failed/Expired` (đọc lại khi nhận push `data_export_ready`) → `download-link` rồi tải **ngay** bằng HTTP client có bearer (link sống 10 phút, dùng một lần) → lưu/chia sẻ ZIP; (3) danh sách các lần xuất. Lỗi chỉ hiện `message`. Màn xoá tài khoản: nhắc tải dữ liệu trước (sau khi xin xoá không đăng nhập được nữa, chỉ lấy được qua hỗ trợ); `DELETE /auth/account` có thể trả 409 `DATA_EXPORT_IN_PROGRESS` khi gói đang dựng → hiện `message`, mời chờ xong rồi xoá (S5). Quá 20 lần xuất CSV/giờ → 429.
