# Kế hoạch: web admin còn thiếu so với backend (2026-11-28)

Nguồn: báo cáo "FinMate — Web admin còn thiếu so với backend" (27/11, @Thien) **đối chiếu lại với mã hiện có**: web admin ở `E:\Ki 9\Capstone_Project\FinMate_Admin` (nhánh `dev/tuan`, `12e7427`), backend ở thư mục làm việc `E:\Ki 9\Model AI\FinMate_BE`. Mọi mục dưới đây là kết quả kiểm, không chép lại báo cáo.

## 1. Kết quả kiểm từng mục của báo cáo

| # | Báo cáo nói | Kiểm trên mã | Kết luận |
|---|---|---|---|
| 1 | Chưa có màn **Tham số dự báo** (`/ops/forecast-config/*`) | Backend có đủ 11 API (`OpsForecastConfigController`: meta, drafts CRUD, submit, withdraw, cancel, approve, reject). Web không có route, không có lời gọi nào tới `forecast-config` | **Đúng, thiếu** |
| 2 | Staff dùng `/ops/catalog/*`, trang Danh mục đang gọi `/admin/categories` nên Staff bị 403 | Backend có `OpsCatalogController` (20 action). Web không có lời gọi `ops/catalog`; route `/categories` chỉ dành cho Admin | **Đúng, thiếu** |
| 3 | Thiếu nút **Từ chối** (giả định kinh tế, kho tri thức), người soạn vẫn thấy nút Duyệt | Backend có `POST /admin/assumptions/{id}/reject` và `/admin/knowledge/documents/{id}/reject`. Web không có lời gọi `/reject` nào cho hai mục này | **Đúng, thiếu** |
| 4 | Trang **KPI** chạy dữ liệu giả | `/overview` → `KpiOverviewPage` đọc `kpiFixtures`/`summarizeKpis` từ `developmentData.ts`. Backend có `GET /ops/kpi/summary` | **Đúng** |
| 5 | Thiếu 4 quyền mới trong `permissions.ts` | Không có `ViewProductKpi`, `MaintainCatalog`, `DraftForecastConfig`, `ApproveForecastConfig` trong web; backend dùng chúng (`OpsCapability.*`) | **Đúng** |
| 6 | Thiếu 3 nhóm cấu hình `loans`, `dataExport`, `kpi` | Không có trong `systemConfigSchema.json` / `AdvancedConfigEditor.tsx` | **Đúng** |
| 7 | **5 API web gọi mà backend không có** (404) | `candidate-topics`, `ai-learning/summary`, `ai-learning/suggestions`, `brand-logo`, `agent-sandbox/run` **đều có trong thư mục backend hiện tại** (`AdminKnowledgeController`, `AdminAiLearningController`, `AdminBrandLogoController`, `AdminAgentSandboxController`) | **Báo cáo đã cũ** (so với `main @ 32912fc`). Có thể các phiên làm việc gần đây vừa thêm; cần xác nhận chúng đã được commit/merge vào `main` (thư mục backend đang chưa commit) |
| 8 | Trang **Đồng bộ dữ liệu**, **Hóa đơn** dùng dữ liệu giả | Đúng (`PendingDataPage` đọc `syncFixtures`/`billFixtures`, chỉ bật khi `import.meta.env.DEV`) | **Đúng** |
| 9 | Thiếu nhỏ: `tickets/{id}/assist`, `subscriptions/features`, `groups/{id}`, `market-sources/bulk` | Backend có `OpsTicketAssistController`, `OpsSubscriptionsController.features`, `AdminMarketDataController.market-sources/bulk`; web không gọi | **Đúng, thiếu** |

Việc 7 đổi bản chất: không phải sửa web để tránh 404, mà là **xác nhận backend đã nằm trong `main`**, rồi giữ các màn đó như cũ.

## 2. Thứ tự làm và cách làm

Mỗi bước độc lập, có thể giao cho một phiên riêng; bước 1 phải xong trước vì các bước sau cần quyền và nhánh.

1. **Hợp nhất nhánh và quyền** (nhỏ)
   - Merge `dev/tuan` ↔ `main` của web admin (báo cáo ghi `main` lệch 124 file); chỉ làm khi người dùng chốt, không tự merge.
   - Thêm 4 quyền vào `shared/permissions.ts` (`CAPABILITY_LABEL`, nhãn tiếng Việt) và gắn menu/route (`need(...)` ở `App.tsx`). Quyền lấy từ `GET /ops/me` như hiện nay.
2. **Duyệt hai người: nút Từ chối** (vừa)
   - `AssumptionsPage.tsx`, `KnowledgePage.tsx`: thêm "Từ chối" (bắt buộc ghi chú), trạng thái `rejected`; ẩn nút Duyệt với bản do chính mình soạn; nút theo quyền `ApproveAgentContent`, không theo vai trò.
3. **Tham số dự báo** (lớn nhất)
   - Trang mới `/ops/forecast-config`: form dựng từ `GET /ops/forecast-config/meta`, chỉ gửi trường đã đổi, hiện diff và kiểm tra trước khi gửi duyệt; hàng chờ cho OpsManager (`?status=pending_approval`); cờ stale/conflicts + "Làm lại"; từ chối bắt buộc ghi chú; hoàn tác bằng `fromVersion`.
   - Quyền: soạn = `DraftForecastConfig`, duyệt = `ApproveForecastConfig`; không ai duyệt bản của mình (backend chặn bằng service và CHECK).
4. **Danh mục cho Staff** (vừa)
   - Trang danh mục dùng `/ops/catalog/*` khi người dùng là Staff (quyền `MaintainCatalog`), Admin giữ `/admin/*` như cũ. Chỉ thêm/sửa/ẩn; không xóa, không đổi slug; xử lý 422 `PROTECTED_CATEGORY` (không ẩn `income`, `loan`, `other`).
5. **KPI thật** (vừa)
   - `KpiOverviewPage` gọi `GET /ops/kpi/summary?from&to&granularity`, quyền `ViewProductKpi` (không dùng `ViewDashboard`); hiển thị ô che `"<5"` đúng như backend trả; bỏ `kpiFixtures`. Không đặt tên "DAU" (backend cố ý không có).
6. **Đồng bộ dữ liệu và Hóa đơn**: ẩn khỏi menu (không có API) hoặc thay bằng số liệu độ phủ từ `GET /ops/monitoring/status`. Chọn: **ẩn**, vì không có API tương ứng.
7. **Schema cấu hình**: sinh lại `systemConfigSchema.json` từ `SystemConfigDocument`, thêm `loans`, `dataExport`, `kpi` vào `advancedGroups`. Không mất dữ liệu (web giữ nhóm nó không biết khi lưu).
8. **Việc nhỏ (nếu kịp)**: gợi ý trả lời ticket (`POST /ops/tickets/{id}/assist`), danh mục tính năng gói (`GET /ops/subscriptions/features`), `GET /ops/groups/{id}`, `market-sources/bulk`.
9. **Xác nhận 5 API "404"**: kiểm `git log`/`git status` ở backend xem 5 controller trên đã commit chưa; nếu chưa, đưa vào danh sách commit của người phụ trách backend. Không đổi gì ở web.

Ngoài phạm vi: hàng chờ yêu cầu dữ liệu cá nhân (DSAR) cần backend làm trước (hạn theo Nghị định 356/2025).

## 3. Rủi ro và quyết định cần chốt

- **Merge `dev/tuan` vào `main`** lệch 124 file: cần người phụ trách web admin quyết (có thể có xung đột).
- **Trang Tham số dự báo** là phần lớn nhất; nên làm sau khi có bước 1.
- **Chuỗi tiếng Việt, nhãn quyền** theo `labels.ts`/`permissions.ts` hiện có; không viết cứng danh sách tham số dự báo (lấy từ `/meta`).
- Test: web có thư mục `tests/`; thêm test cho từng màn mới và cho nút Duyệt/Từ chối theo quyền.
- Chỉ build/chạy khi người dùng yêu cầu rõ; DB dev dùng chung.

## 4. Kế hoạch phần còn lại sau lần làm bước 2–8 (2026-11-28)

Đã kiểm trên mã; mỗi việc nhỏ, độc lập, theo thứ tự nên làm (từ rẻ và ít xung đột đến tốn công hơn).

| # | Việc | Hiện trạng đã kiểm | Cách làm | Rủi ro |
|---|---|---|---|---|
| A | **Danh mục tính năng gói** (`GET /ops/subscriptions/features`) | Backend có (`OpsSubscriptionsController.Features`, trả `SubscriptionFeatureDefinition[]`). Web `SubscriptionsPage.tsx` có `FeatureEditor` nhưng tính năng đang tự dựng từ chính gói, không lấy danh mục từ server | Trong `FeatureEditor` gọi `/ops/subscriptions/features`, hiện đủ tính năng sellable (kể cả tính năng gói chưa có) kèm mô tả; lưu vẫn `PUT plans/{id}/features` (tính năng không gửi = không có trong gói) | Thấp; kiểm kiểu `SubscriptionFeatureDefinition` trước khi viết |
| B | **Tra cứu nhóm khi xử lý ticket** (`GET /ops/groups/{id}`) | Backend có ở `OpsManagementController` (dòng 66). Web chưa gọi; ticket chưa có chỗ nhập id nhóm | Trong ca hỗ trợ, ô "Tra nhóm" (nhập id nhóm do khách cung cấp) → hiện thông tin nhóm backend cho phép; nếu API cần quyền/ca liên quan thì hiện lỗi 403/404 nguyên văn | Cần đọc `docs/ops-api.md` về quyền (chỉ ops có ca liên quan); không đưa dữ liệu thành viên vượt những gì API trả |
| C | **Thêm nhiều nguồn thị trường** (`POST /admin/market-sources/bulk`) | Backend có, body `{ urls: string[] }`, trả `BulkSourceResult[]` (`url, created, source, warnings, errorCode, errorMessage`). `MarketPage.tsx` chỉ thêm từng nguồn | Nút "Thêm nhiều nguồn": ô dán mỗi dòng một URL → gọi bulk → bảng kết quả (đã tạo / cảnh báo / lỗi từng dòng) | **Phiên "dữ liệu thị trường theo mục tiêu" cũng sẽ sửa `MarketPage.tsx`**: làm sau khi phiên đó xong hoặc phối hợp, tránh xung đột |
| D | **Schema cấu hình còn thiếu** | So `SystemConfigDocument.cs` với `systemConfigSchema.json`: thiếu kiểu `OpsNotificationSettings` (`enabled`, `disabledTypes`, `retentionDays`), `GoldSettings` (8 trường), `GoalMarketAlertSettings` (3 trường); thiếu trường ở `GroupSettings` (6), `MarketDataSettings` (7: `trustedDomains`, `gold`, `goalTopics`, `goalAlerts`, `assumptionRules`, `fuelReleaseLinkText`, `fuelReleaseNames`), `FinanceMethodSettings` (`planWarningTexts`); thiếu khóa `opsNotifications` ở `SystemConfigDocument` | Làm như lần trước: thêm kiểu và trường (kiểu, mặc định, min/max theo `SystemConfigDocumentValidator`) + nhãn tiếng Việt + nhóm `opsNotifications` vào `advancedGroups`. **Nên viết script sinh schema từ C#** thay vì sửa tay để lần sau không lệch lại | Hai phiên khác (thông báo nội bộ, dữ liệu thị trường) vẫn đang thêm trường: chốt sau khi họ xong, rồi chạy lại script |

Làm xong từng việc thì chạy `npx tsc -b`, `npx eslint src`, `npm run build`; chỉ build/chạy thử khi người dùng yêu cầu.

Đề xuất thực hiện: A và B ngay (không đụng phiên khác); C và D sau khi hai phiên liên quan báo xong, kèm script sinh schema.
