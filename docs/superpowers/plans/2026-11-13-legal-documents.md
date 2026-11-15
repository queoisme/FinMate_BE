# Văn bản pháp lý cập nhật được — kế hoạch triển khai

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đọc Điều khoản và Chính sách quyền riêng tư từ server (công khai), cho Manager soạn nháp và chỉ Admin xuất bản; xuất bản đổi phiên bản đồng ý.

**Architecture:** Bảng `legal_versions` (mỗi dòng một phiên bản chứa cả hai văn bản, tối đa một dòng nháp) thay cho hằng `ConsentVersions.Terms`. Một `ILegalVersionProvider` đọc phiên bản hiện hành từ DB mỗi lần. Hai service (đọc công khai; soạn/xuất bản) theo mẫu `OpsSubscriptionService`, quyền kiểm tra qua `IOpsAccessService` đọc DB.

**Tech Stack:** ASP.NET Core 9, EF Core + Npgsql (jsonb), FluentValidation, xUnit + Testcontainers PostgreSQL (mỗi `AuthApiFactory` có DB riêng).

**Spec:** `docs/superpowers/specs/2026-11-13-legal-documents-design.md`

## Global Constraints
- Lệnh chạy từ `backend/`: `dotnet build`, `dotnet test --filter FullyQualifiedName~<Tên>`. Máy không có .NET 9 SDK thì chạy trong `mcr.microsoft.com/dotnet/sdk:9.0` với **gốc repo** được mount (`FinMate.Application` nhúng `../../ocr-service/data/shared_words.json`).
- Migration: `dotnet ef migrations add AddLegalVersions --project FinMate.Infrastructure --startup-project FinMate.API`.
- ID là `Guid`; thời gian là `DateTimeOffset`/`TIMESTAMPTZ`; cột đặt tên `snake_case`.
- Dòng `published` không bao giờ sửa. Tối đa **một** dòng `draft` (unique index một phần).
- Giới hạn đầu vào: mỗi văn bản 1–50 mục; `title` ≤ 200, `body` ≤ 20 000 ký tự, đều không rỗng. `body` là văn bản thuần, lưu và trả nguyên văn.
- Quyền: soạn/xem nháp/lịch sử cần `OpsCapability.ManageLegalDocuments` (`1 << 17`, thuộc `ManagerCapabilities`); xuất bản cần `OpsCapability.ManageSystem`. Không lặp `[Authorize(AdminOnly)]` ở controller admin (kế thừa `AdminControllerBase`).
- Không ghi nội dung văn bản vào audit log, chỉ ghi id nháp / số phiên bản.
- Mỗi lần xuất bản là phiên bản mới (`max đã xuất bản + 1`), kể cả sửa chữ nhỏ.
- Chỉ `git add` từng đường dẫn cụ thể; không `git add .`.

## Review Focus
1. Hai Admin bấm xuất bản cùng lúc: chỉ một phiên bản mới ra đời, người kia nhận 409.
2. Client gửi lại đúng `updatedAt` vừa nhận từ `PUT` cho lần `PUT` kế tiếp phải thành công (Postgres cắt về micro giây, .NET giữ 100ns; lệch là 409 giả).
3. `GET legal/*` ẩn danh không bao giờ lộ nội dung hay số phiên bản của bản nháp.
4. Manager bị hạ vai trò mất quyền ngay dù token còn hạn (quyền đọc từ DB).
5. Nháp giống hệt bản hiện hành bị từ chối, phiên bản không đổi (xuất bản mà không đổi gì vẫn buộc cả hệ thống đồng ý lại).

## File Structure
- Create `backend/FinMate.Domain/Entities/LegalVersion.cs` — entity + `LegalSection` + `LegalVersionStatus`.
- Create `backend/FinMate.Application/Legal/LegalModels.cs` — DTO, input, `LegalErrorCodes`, validator.
- Create `backend/FinMate.Application/Legal/LegalService.cs` — `ILegalRepository`, `ILegalVersionProvider`, `ILegalDocumentService`, `ILegalAdminService` và cài đặt.
- Create `backend/FinMate.Infrastructure/Persistence/Configurations/LegalVersionConfiguration.cs`, `.../Repositories/LegalRepository.cs`, migration `AddLegalVersions`.
- Create `backend/FinMate.API/Controllers/LegalController.cs`, `Controllers/Ops/OpsLegalController.cs`, `Controllers/Admin/AdminLegalController.cs`.
- Modify: `FinMateDbContext.cs`, `Program.cs` (đăng ký DI cạnh dòng 496–498), `OpsPermissions.cs`, `AuditEvents.cs`, `User.cs`, `UserConfiguration.cs`, `UserProfileDto.cs`, `ConsentCommandHandler.cs`, `GetUserProfileQueryHandler.cs`, `RegisterCommandHandler.cs`, `UpdateUserProfileCommandHandler.cs`, `ConsentVersions.cs`, các test dùng `ConsentVersions.Terms`.
- Test: `backend/FinMate.Tests/Integration/Controllers/LegalDocumentsTests.cs` (mới; chép các hàm trợ giúp `LoginAsync`, `RegisterAsync`, `SetRoleAsync`, `AccountWithRoleAsync`, `Request` từ `SubscriptionsTests.cs:152-221` vì chúng là `private`).

---

### Task 1: Lớp dữ liệu và `ILegalVersionProvider`

**Files:**
- Create: `LegalVersion.cs`, `LegalModels.cs` (chỉ `LegalErrorCodes` lúc này), `LegalService.cs` (chỉ `ILegalRepository`, `ILegalVersionProvider` + `LegalVersionProvider`), `LegalVersionConfiguration.cs`, `LegalRepository.cs`, migration
- Modify: `FinMateDbContext.cs`, `Program.cs`
- Test: `LegalDocumentsTests.cs`

**Interfaces:**
- Produces:
  - `LegalVersion { Guid Id; int? Version; LegalVersionStatus Status; List<LegalSection> TermsSections; List<LegalSection> PrivacySections; Guid CreatedBy; DateTimeOffset UpdatedAt; Guid? PublishedBy; DateTimeOffset? PublishedAt }`, `record LegalSection(string Title, string Body)`, `enum LegalVersionStatus { Draft, Published }`.
  - `ILegalRepository`: `Task<int?> GetCurrentVersionAsync(CancellationToken)`; `Task<LegalVersion?> GetCurrentPublishedAsync(CancellationToken)` (không tracking); `Task<LegalVersion?> GetDraftAsync(CancellationToken)` (tracking); `Task<IReadOnlyList<LegalVersion>> ListAsync(CancellationToken)` (mới nhất trước, không tracking); `void Add(LegalVersion)`; `Task SaveChangesAsync(CancellationToken)`.
  - `ILegalVersionProvider.CurrentTermsVersionAsync(CancellationToken ct = default) : Task<int>`.
  - `LegalErrorCodes`: `DraftStale = "legal_draft_stale"`, `NoDraft = "legal_no_draft"`, `DraftUnchanged = "legal_draft_unchanged"`.

- [ ] **Step 1: Viết test thất bại** — `Provider_OnFreshDatabase_ReturnsSeededVersion4` trong `LegalDocumentsTests` (dùng `IClassFixture<AuthApiFactory>`): lấy `ILegalVersionProvider` từ `factory.Services.CreateScope()`, `await CurrentTermsVersionAsync()` bằng `4`; đọc `ILegalRepository.GetCurrentPublishedAsync()` thì `TermsSections` và `PrivacySections` đều có đúng 4 mục.
- [ ] **Step 2: Chạy và xác nhận thất bại** — `dotnet test --filter FullyQualifiedName~LegalDocumentsTests` → lỗi biên dịch (chưa có kiểu).
- [ ] **Step 3: Cài đặt entity, configuration, DbSet, repository, provider.** Bảng `legal_versions`: cột theo spec; `status` lưu theo cách `SubscriptionOrderStatus` đang lưu; hai cột section `jsonb`; unique index `uq_legal_versions_version` trên `version` (bỏ qua null); unique index một phần `uq_legal_versions_single_draft` đảm bảo tối đa một dòng `status = draft`; `UpdatedAt` là concurrency token. `LegalRepository.SaveChangesAsync` bắt `DbUpdateConcurrencyException` và ném `ConflictException(LegalErrorCodes.DraftStale, ...)`. Đăng ký `ILegalRepository`, `ILegalVersionProvider` scoped trong `Program.cs`.
- [ ] **Step 4: Tạo migration `AddLegalVersions`** rồi thêm dòng bản 4 `published` vào `Up` bằng `migrationBuilder.Sql`/`InsertData`: `version = 4`, `created_by` = Guid rỗng, `published_at`/`updated_at` = thời điểm migration, các mục lấy **nguyên văn** từ `FinMate_FE/lib/features/profile/presentation/screens/legal_document_screen.dart` dòng 31–34 (Điều khoản) và 37–40 (Chính sách). Xác nhận `Down` xóa bảng.
- [ ] **Step 5: Chạy test** → PASS.
- [ ] **Step 6: Commit** — thêm từng file đã tạo/sửa ở Task này và migration + `Designer.cs` + `FinMateDbContextModelSnapshot.cs`; message `feat(legal): legal_versions table and version provider`.

---

### Task 2: Thay `ConsentVersions.Terms` bằng provider

**Files:**
- Modify: `User.cs:111`, `UserConfiguration.cs:98`, `UserProfileDto.cs`, `ConsentCommandHandler.cs`, `GetUserProfileQueryHandler.cs`, `RegisterCommandHandler.cs`, `UpdateUserProfileCommandHandler.cs`, `ConsentVersions.cs`
- Test: `ConsentCommandHandlerTests.cs` (17 chỗ), `ConsentControllerTests.cs` (9 chỗ), mọi test khác báo lỗi biên dịch

**Interfaces:**
- Consumes: `ILegalVersionProvider.CurrentTermsVersionAsync` (Task 1).
- Produces: `User.HasAcceptedTerms(int currentVersion) : bool` (thay thuộc tính `TermsAccepted`); `UserProfileDto.From(User user, int termsVersion)`.

- [ ] **Step 1: Sửa test trước.** Trong `ConsentCommandHandlerTests`: thêm `Mock<ILegalVersionProvider>` trả `4`, truyền vào constructor handler, thay `ConsentVersions.Terms` bằng hằng cục bộ `CurrentTerms = 4`, thay `_user.TermsAccepted` bằng `_user.HasAcceptedTerms(CurrentTerms)`. Thêm test `AcceptTerms_WhenProviderVersionMoved_RejectsOldVersionWith409`: provider trả `5`, gửi `4` → `ConflictException` mã `AuthErrorCodes.ConsentVersionOutdated`. Trong `ConsentControllerTests` thay hằng bằng `4` (bản seed).
- [ ] **Step 2: Chạy** `dotnet test --filter "FullyQualifiedName~Consent"` → thất bại/biên dịch lỗi.
- [ ] **Step 3: Cài đặt.** `HasAcceptedTerms` thay thuộc tính (xóa `builder.Ignore(u => u.TermsAccepted)` ở `UserConfiguration.cs:98`). `UserProfileDto.From` nhận thêm `termsVersion`; bốn handler gọi nó đều tiêm `ILegalVersionProvider` và truyền `await _legal.CurrentTermsVersionAsync(ct)`. `ConsentCommandHandler`: nhánh `ConsentKind.Terms` lấy `current` từ provider. Xóa `ConsentVersions.Terms` (giữ `Ai`, `AiOptedOut`, `Training`, `SurveyAi`, `Community`) và cập nhật doc-comment trỏ về `legal_versions`. Sửa mọi chỗ còn lỗi biên dịch trong thư mục Tests.
- [ ] **Step 4: Chạy toàn bộ** `dotnet test` → PASS, không test nào mới đỏ so với trước khi sửa (CLAUDE.md ghi sẵn một test đỏ về `rule_80_20` từ Phase 52; không tính).
- [ ] **Step 5: Commit** — các file đã sửa (liệt kê từng đường dẫn); message `refactor(consent): read current terms version from legal_versions`.

---

### Task 3: Đọc công khai `GET api/v1/legal/{terms|privacy}`

**Files:**
- Create: `LegalController.cs`
- Modify: `LegalModels.cs` (thêm `LegalSectionDto`, `LegalDocumentDto`), `LegalService.cs` (thêm `ILegalDocumentService` + cài đặt), `Program.cs`
- Test: `LegalDocumentsTests.cs`

**Interfaces:**
- Consumes: `ILegalRepository.GetCurrentPublishedAsync`.
- Produces: `enum LegalDocumentKind { Terms, Privacy }`; `ILegalDocumentService.GetPublishedAsync(LegalDocumentKind kind, CancellationToken ct = default) : Task<LegalDocumentDto>`; `record LegalSectionDto(string Title, string Body)`; `record LegalDocumentDto(int Version, DateTimeOffset UpdatedAt, IReadOnlyList<LegalSectionDto> Sections)` (`UpdatedAt` = `PublishedAt`).

- [ ] **Step 1: Test thất bại.** `AnonymousCanReadTermsAndPrivacy`: không token, `GET api/v1/legal/terms` và `/privacy` trả 200, `data.version == 4`, `data.sections.Count == 4`, mục đầu có `title` không rỗng. `RoutesAreAnonymousAndOnlyGet`: `POST api/v1/legal/terms` không được là 200.
- [ ] **Step 2: Chạy** → 404.
- [ ] **Step 3: Cài đặt.** `[ApiController][AllowAnonymous][Route("api/v1/legal")]`, hai action `[HttpGet("terms")]`, `[HttpGet("privacy")]` trả `ApiResponse<LegalDocumentDto>.Ok(...)`. Service đọc `GetCurrentPublishedAsync` (không bao giờ đọc nháp) và chọn danh sách mục theo `kind`. Đăng ký DI.
- [ ] **Step 4: Chạy** → PASS.
- [ ] **Step 5: Commit** — `feat(legal): public GET legal/terms and legal/privacy`.

---

### Task 4: Soạn nháp (Manager) và quyền `ManageLegalDocuments`

**Files:**
- Create: `OpsLegalController.cs`
- Modify: `OpsPermissions.cs`, `AuditEvents.cs`, `LegalModels.cs`, `LegalService.cs`, `Program.cs`
- Test: `LegalDocumentsTests.cs`

**Interfaces:**
- Consumes: `IOpsAccessService.RequireAsync(Guid actorId, OpsCapability, CancellationToken)`, `IAuditLogService.LogAsync(string event, Guid actorId, string? ip, object data, CancellationToken)`, `ILegalRepository`.
- Produces:
  - `OpsCapability.ManageLegalDocuments = 1 << 17` (thêm vào `ManagerCapabilities`); `AuditEvents.LegalDraftSaved = "Ops.LegalDraft.Saved"`, `AuditEvents.LegalVersionPublished = "Admin.LegalVersion.Published"`.
  - `record LegalDraftDto(bool IsDraft, DateTimeOffset? UpdatedAt, IReadOnlyList<LegalSectionDto> TermsSections, IReadOnlyList<LegalSectionDto> PrivacySections)`; `record LegalVersionSummaryDto(int? Version, string Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, Guid? PublishedBy)`; `record SaveLegalDraftInput(IReadOnlyList<LegalSectionDto>? TermsSections, IReadOnlyList<LegalSectionDto>? PrivacySections, DateTimeOffset? BaseUpdatedAt)` + `SaveLegalDraftInputValidator` (giới hạn ở Global Constraints; cả hai danh sách bắt buộc).
  - `ILegalAdminService`: `ListVersionsAsync(Guid actorId, CancellationToken)`, `GetDraftAsync(Guid actorId, CancellationToken)`, `SaveDraftAsync(Guid actorId, SaveLegalDraftInput input, string? ip, CancellationToken)` trả `LegalDraftDto`.

- [ ] **Step 1: Test thất bại** (mỗi test một hàm):
  - `Manager_CanSaveAndReadDraft`: Manager `PUT api/v1/ops/legal/draft` 2 mục mỗi văn bản → 200, `isDraft = true`, `updatedAt` khác null; `GET .../draft` trả đúng nội dung vừa lưu.
  - `GetDraft_WithoutDraft_ReturnsCurrentPublishedAsStartingPoint`: `isDraft = false`, `updatedAt = null`, 4 mục.
  - `Staff_IsForbidden_UntilAdminGrantsCapability`: Staff GET `versions` → 403.
  - `DemotedManager_LosesAccessImmediately`: Manager gọi được → Admin hạ về `User` qua cách `SubscriptionsTests` đang dùng → cùng token gọi lại → 403.
  - `SaveDraft_WithStaleBaseUpdatedAt_Returns409`: lưu lần 1 lấy `updatedAt` T1, lưu lần 2 (đúng T1) lấy T2, lưu lần 3 với `baseUpdatedAt = T1` → 409 mã `legal_draft_stale`, nháp vẫn là nội dung lần 2.
  - `SaveDraft_EchoingTheReturnedUpdatedAt_AlwaysSucceeds`: lưu 3 lần liên tiếp, mỗi lần gửi `updatedAt` vừa nhận → cả ba 200.
  - `SaveDraft_Validation`: danh sách rỗng, `title` 201 ký tự, `body` 20 001 ký tự, `body` rỗng → 422 mỗi trường hợp.
  - `Body_WithHtml_IsStoredVerbatim`: `body = "<script>alert(1)</script>"` đọc lại đúng chuỗi đó.
- [ ] **Step 2: Chạy** → 404/403 hàng loạt.
- [ ] **Step 3: Cài đặt.** `OpsLegalController : OpsControllerBase`, `[Route("api/v1/ops/legal")]`, `[Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]`; `GET versions`, `GET draft`, `PUT draft`. `SaveDraftAsync`: `RequireAsync(ManageLegalDocuments)` → validate → nếu có nháp thì `input.BaseUpdatedAt` phải bằng `draft.UpdatedAt` (không thì `ConflictException(DraftStale)`), nếu chưa có thì bỏ qua → ghi đè hai danh sách, `UpdatedAt = now` **cắt về micro giây** (`now.AddTicks(-(now.Ticks % 10))`), `CreatedBy = actorId` khi tạo → lưu → audit `LegalDraftSaved` (chỉ `draftId`). Đăng ký DI.
- [ ] **Step 4: Chạy** → PASS. Chạy thêm `dotnet test --filter FullyQualifiedName~RolePermissions` để chắc quyền mới không làm hỏng ma trận hiện có.
- [ ] **Step 5: Commit** — `feat(legal): managers draft legal documents`.

---

### Task 5: Xuất bản (Admin) và tài liệu

**Files:**
- Create: `AdminLegalController.cs`
- Modify: `LegalModels.cs`, `LegalService.cs`, `docs/ops-api.md` (ma trận quyền + endpoint), `docs/REQUIREMENTS.md` (FR-AUTH-00, FR-CONS-01: bản hiện hành lấy từ `legal_versions`), `CLAUDE.md` (một đoạn ngắn: phiên bản Điều khoản nằm ở DB, không còn là hằng)
- Test: `LegalDocumentsTests.cs`

**Interfaces:**
- Consumes: `ILegalAdminService` (Task 4).
- Produces: `ILegalAdminService.PublishAsync(Guid actorId, string? ip, CancellationToken) : Task<PublishedLegalVersionDto>`; `record PublishedLegalVersionDto(int Version, DateTimeOffset PublishedAt)`; `AdminLegalController` `[Route("api/v1/admin/legal")]`, `[HttpPost("draft/publish")]` trả 200.

- [ ] **Step 1: Test thất bại** (mọi test đọc phiên bản hiện hành trước rồi so tương đối, vì các test trong class chung một DB):
  - `Manager_CanNotPublish`: Manager `POST admin/legal/draft/publish` → 403; sau đó `GET legal/terms` vẫn trả phiên bản cũ.
  - `Admin_Publish_AssignsNextVersion_AndPublicReadShowsIt`: Manager lưu nháp; Admin xuất bản → 200, `version = cũ + 1`; `GET legal/terms` trả `version` mới và đúng nội dung vừa soạn; `GET versions` (Manager) không còn dòng `draft`.
  - `Publish_WithoutDraft_Returns422_NoDraft` (mã `legal_no_draft`).
  - `Publish_DraftIdenticalToCurrent_Returns422_AndKeepsVersion`: lấy bản hiện hành qua `GET draft` rồi `PUT` lại y nguyên, xuất bản → 422 mã `legal_draft_unchanged`, `GET legal/terms` không đổi version.
  - `Anonymous_NeverSeesDraft`: lưu nháp có chuỗi đánh dấu duy nhất; `GET legal/terms` và `/privacy` không chứa chuỗi đó, `version` không đổi.
  - `PublishingInvalidatesOldConsent`: người dùng đồng ý bản hiện hành (`POST users/me/consents` loại `terms`) → `termsAccepted = true`; Admin xuất bản bản mới → `GET users/me` trả `termsAccepted = false`; gửi lại đồng ý với version cũ → 409 (`AuthErrorCodes.ConsentVersionOutdated`); gửi đúng version mới → `termsAccepted = true`.
  - `TwoAdminsPublishingAtOnce_YieldExactlyOneNewVersion`: một nháp, bắn hai request xuất bản song song bằng `Task.WhenAll` → đúng một 200 và một 409/422; version hiện hành = cũ + 1.
- [ ] **Step 2: Chạy** → 404/403.
- [ ] **Step 3: Cài đặt.** `AdminLegalController : AdminControllerBase`. `PublishAsync`: `RequireAsync(ManageSystem)` → lấy nháp (không có → `BusinessRuleException(NoDraft)`) → so với bản hiện hành bằng `SequenceEqual` trên cả hai danh sách (bằng nhau → `BusinessRuleException(DraftUnchanged)`) → `Version = currentMax + 1`, `Status = Published`, `PublishedBy`, `PublishedAt = UpdatedAt = now` → `SaveChangesAsync` (đua nhau thì concurrency token ở Task 1 làm người sau nhận `ConflictException(DraftStale)`) → audit `LegalVersionPublished` (chỉ số phiên bản). Cập nhật ba tài liệu.
- [ ] **Step 4: Chạy** `dotnet test` toàn bộ → PASS (trừ test đỏ có sẵn nói ở Task 2).
- [ ] **Step 5: Commit** — `feat(legal): admins publish legal versions, consent follows`.

---

## Tự kiểm tra
- **Phủ spec:** dữ liệu, bản 4 seed (T1); thay hằng + ba chỗ đọc (T2); GET công khai (T3); quyền, nháp, `baseUpdatedAt`, validate, audit (T4); xuất bản, chống trùng/nháp trống, hậu quả đồng ý, tài liệu (T5).
- **Nhất quán tên:** `HasAcceptedTerms`, `ILegalVersionProvider.CurrentTermsVersionAsync`, `LegalErrorCodes.{DraftStale,NoDraft,DraftUnchanged}`, `ManageLegalDocuments` dùng giống nhau ở mọi task.
- **Ngoài phạm vi (theo spec):** sửa FE, nội dung bản 5, thông báo "điều khoản đã đổi", cờ "sửa nhỏ không hỏi lại".
