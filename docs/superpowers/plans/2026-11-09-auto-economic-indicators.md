# Tự cập nhật giả định kinh tế (CPI) và chỉ báo thị trường (giá xăng) cho dự báo

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dự báo mục tiêu dài hạn luôn dùng số kinh tế mới nhất: lạm phát tự cập nhật hằng tháng từ nguồn chính thức, giá xăng theo tuần làm "chỉ báo thị trường" nới dải dự báo khi thị trường biến động, mọi thay đổi có nguồn, ngày, nhật ký.

**Architecture:** Hai lớp tách bạch. (1) *Giả định dài hạn* (`economic_assumptions`, tốc độ tăng hằng năm, đã có phiên bản/duyệt) do một job hằng tháng tạo bản mới từ nguồn cấu hình; đổi lớn thì để Draft cho người duyệt. (2) *Chỉ báo thị trường hiện tại* (bảng mới `market_indicators`, chuỗi theo thời gian) do job hằng tuần cập nhật; nó KHÔNG ghi đè giả định dài hạn mà chỉ nới độ rộng dải/kịch bản bảo thủ và gắn nhãn "thị trường đang biến động". Số tiền vẫn do code tính, LLM không sinh số.

**Tech Stack:** ASP.NET Core 9, Hangfire (đã có 10 job định kỳ), EF Core + migration, `IAssumptionService`, system config (nguồn dữ liệu cấu hình, không viết cứng), hệ thống giám sát sự cố sẵn có.

**Spec:** `docs/agent-api.md`, `docs/agent-content-checklist.md` mục 1, `docs/knowledge-drafts/README.md`; hành vi hiện tại: `AdminAssumptionsController` (phiên bản, duyệt, tự gỡ bản cũ), `AssumptionService`, `GoalSimulator` (giá mục tiêu tăng theo `annualBps/12`).

## Kết quả kiểm nguồn (2026-11-09, trước khi viết plan)

- **CPI:** thông cáo tháng của Cục Thống kê (`https://www.nso.gov.vn/?p=62845` cho tháng 10/2026) là trang WordPress, chữ hiển thị sẵn ("Bình quân mười tháng năm 2026, CPI tăng 4,57% so với cùng kỳ năm trước"), kèm tệp PDF và XLSX; trang danh sách `https://www.nso.gov.vn/tin-tuc-khac`. Không thấy API/RSS. Có thể đọc bằng code nhưng dễ gãy khi đổi giao diện.
- **Giá xăng:** do liên Bộ Công Thương – Tài chính điều hành theo kỳ (định kỳ thứ Năm hằng tuần, có thể điều chỉnh bất thường khi cơ sở tăng ≥ 15% hoặc giảm ≥ 10%); nguồn gốc là công văn của Bộ Công Thương. Các trang tìm thấy (thuvienphapluat) chỉ là tổng hợp. **Chưa có nguồn đọc tự động ổn định đã kiểm chứng.**

## Quyết định cần chủ dự án chốt (đề xuất mặc định)

1. **Nguồn CPI:** trang thông cáo của Cục Thống kê, đọc phần chữ (không đọc XLSX/PDF). Địa chỉ trang danh sách đặt trong system config `economicFeeds`, sửa được ở admin.
2. **Tự duyệt hay chờ người:** bản CPI mới tự động Approved nếu chênh so với bản đang dùng ≤ 100 điểm cơ bản (1 điểm phần trăm); lớn hơn thì để Draft và báo OpsManager. Ngưỡng nằm trong config.
3. **Giá xăng:** CHƯA làm tự động cho đến khi chọn được nguồn (Manager quyết: công văn Bộ Công Thương, bảng giá Petrolimex, hay nhập tay hằng tuần qua trang admin). Task 5 là cổng quyết định; Task 6–8 phụ thuộc.
4. **Tác động của chỉ báo xăng lên dự báo:** chỉ nới độ rộng dải và kịch bản bảo thủ, không đổi giả định dài hạn; hệ số tính từ biến động giá 4 tuần gần nhất, tham số nằm trong config.
5. **Thông báo người dùng khi kết luận mục tiêu đổi:** để sau (Task 9, tùy chọn), không nằm trong đợt đầu.

## Global Constraints

- LLM không sinh số tiền; mọi số vào dự báo đến từ bảng giả định/chỉ báo do code đọc; mỗi giá trị có `source_name`, `source_url` (trang cụ thể), `as_of`.
- Nguồn dữ liệu (URL, mẫu đọc, ngưỡng, lịch) nằm trong system config/DB, không hard-code trong mã.
- Job không bao giờ ném lỗi làm hỏng host; nguồn lỗi hoặc đổi định dạng thì giữ bản cũ và mở sự cố qua hệ thống giám sát; không ghi nội dung trang tải về vào log.
- Mọi bản tự tạo ghi audit (actor hệ thống); số tự duyệt phải nằm trong khoảng hợp lý cấu hình (`rateBps` trong [−2.000, 5.000]), ngoài khoảng thì Draft.
- Tôn trọng điều khoản sử dụng và tần suất truy cập hợp lý của nguồn: một lần kiểm tra mỗi chu kỳ, có `User-Agent` nhận diện, `timeout`, không truy cập lặp.
- Tiền là `long` VND, không `decimal/double`; phần trăm là `rateBps` nguyên.
- Làm trong worktree riêng từ `dev/tuan`; `dotnet test` chỉ trong worktree; không push; `git add` từng đường dẫn.

## Review Focus

1. Trang nguồn đổi giao diện hoặc trả nội dung lạ: job phải thất bại an toàn (không tạo giả định rác), giữ bản cũ, mở sự cố.
2. Số đọc được hợp lệ nhưng đột biến (ví dụ 45,7% thay vì 4,57%): phải rơi vào Draft chờ người, không tự duyệt.
3. Job chạy hai lần trong cùng chu kỳ (restart, retry): không tạo bản trùng cho cùng kỳ công bố.
4. Chỉ báo xăng thiếu dữ liệu vài tuần liền: hệ số nới dải phải về trung tính và giảm độ tin cậy, không mãi dùng số cũ.
5. Giả định đổi bản giữa lúc người dùng đang xem dự báo: kết quả mỗi lượt vẫn nhất quán (một lượt dùng đúng một phiên bản).

---

### Task 1: Cấu hình nguồn dữ liệu kinh tế

**Files:**
- Modify: `backend/FinMate.Application/Admin/SystemConfig/SystemConfigDocument.cs` (thêm nhóm `EconomicFeeds`), `SystemConfigDocumentValidator.cs`, `SystemConfigService.cs` (danh sách mục lịch sử)
- Test: test của system config hiện có (`grep -rl SystemConfigDocumentValidator backend/FinMate.Tests`)

**Interfaces:**
- Produces: `EconomicFeedsSettings { string CpiIndexUrl; int AutoApproveMaxDeltaBps; int MinRateBps; int MaxRateBps; }` mặc định rỗng/0 (job tắt khi `CpiIndexUrl` rỗng); validator: URL https hợp lệ hoặc rỗng, `Min < Max`, `AutoApproveMaxDeltaBps ≥ 0`.

- [ ] **Step 1: Viết test** `Validator_AcceptsEmptyEconomicFeeds`, `Validator_RejectsNonHttpsCpiUrl`, `Validator_RejectsInvertedRateBounds`; test tài liệu cũ không có nhóm `economicFeeds` vẫn đọc được (mặc định rỗng).
- [ ] **Step 2: Chạy** `cd backend && dotnet test FinMate.Tests --filter FullyQualifiedName~SystemConfig`. Expected: FAIL.
- [ ] **Step 3: Cài** nhóm cấu hình, validator, mục lịch sử; không seed giá trị mặc định có URL.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(config): economic feeds settings, off by default`.

### Task 2: Bộ đọc thông cáo CPI (hàm thuần, có dữ liệu mẫu)

**Files:**
- Create: `backend/FinMate.Application/Economics/CpiReleaseParser.cs`
- Create: `backend/FinMate.Tests/Fixtures/cpi-release-2026-10.html` (lưu nguyên trang thông cáo tháng 10/2026 làm dữ liệu mẫu), `backend/FinMate.Tests/Unit/Application/Economics/CpiReleaseParserTests.cs`

**Interfaces:**
- Produces: `public sealed record CpiRelease(int Year, int Month, int AveragePercentBps, int YearOnYearBps, int MonthOnMonthBps, DateOnly PublishedOn);` và `public static CpiRelease? TryParse(string html)` — đọc câu "Bình quân … CPI tăng X% so với cùng kỳ" (X dạng "4,57" → 457 bps), "so với cùng kỳ năm trước", "so với tháng trước"; trả `null` khi không tìm đủ.

- [ ] **Step 1: Viết test**: `ParsesOctober2026Release` (kỳ 10/2026: bình quân 457, cùng kỳ 509, tháng trước 20), `ReturnsNullOnUnrelatedPage`, `ReturnsNullWhenAveragePhraseMissing`, `AcceptsDotOrCommaDecimal`, `RejectsImplausibleValue` (≥ 5.000 bps → null).
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~CpiReleaseParserTests`. Expected: FAIL.
- [ ] **Step 3: Cài** bằng biểu thức chính quy trên văn bản đã bỏ thẻ HTML; số thập phân bằng số nguyên bps (không `double`).
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(economics): CPI release parser with fixture`.

### Task 3: Job làm mới giả định CPI hằng tháng

**Files:**
- Create: `backend/FinMate.Infrastructure/Jobs/EconomicAssumptionRefreshJob.cs`, `backend/FinMate.Application/Economics/IEconomicFeedClient.cs` (+ cài đặt `HttpClient`)
- Modify: `backend/FinMate.API/Program.cs` (đăng ký job và lịch), `docs/ops-api.md` (mục job)
- Test: `backend/FinMate.Tests/Unit/Infrastructure/EconomicAssumptionRefreshJobTests.cs`

**Interfaces:**
- Consumes: Task 1, Task 2, `IAssumptionService.CreateDraftAsync`/`ApproveAsync`/`ListAsync`, hệ thống sự cố giám sát (`OpsErrorFactory`, không đọc `Exception.Message`).
- Produces: job chạy ngày 6 hằng tháng 09:00 giờ VN (sau khi Cục Thống kê công bố) và có thể gọi tay; tạo bản `general_price_growth` với `RateBps = AveragePercentBps`, `Unit = "pct_per_year"`, `SourceName` = tiêu đề thông cáo, `SourceUrl` = trang đã đọc, `AsOf` = ngày công bố. Tự duyệt khi `|Δ| ≤ AutoApproveMaxDeltaBps` và trong [Min, Max]; ngược lại để Draft và mở sự cố mức Normal.

- [ ] **Step 1: Viết test** (client giả): `CreatesDraftAndAutoApprovesSmallChange`, `LargeChangeStaysDraftAndRaisesIncident`, `OutOfBoundsRateStaysDraft`, `SameReleaseTwiceCreatesNothingSecond` (khóa theo ngày công bố), `UnparseablePageKeepsOldAndRaisesIncident`, `DisabledWhenUrlEmpty`.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~EconomicAssumptionRefreshJobTests`. Expected: FAIL.
- [ ] **Step 3: Cài** job; tìm thông cáo mới nhất từ trang danh sách (`CpiIndexUrl`), tải trang con có tiêu đề chứa "Chỉ số giá tiêu dùng", đọc bằng `CpiReleaseParser`; timeout 20 s, một lần mỗi lần chạy; ghi audit.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(economics): monthly CPI assumption refresh job`.

### Task 4: Kiểm nguồn CPI thật (chạy một lần, không ghi gì)

**Files:**
- Create: `docs/economic-feeds-probe.md`

- [ ] **Step 1: Gọi** `CpiReleaseParser` trên trang thật của 3 tháng gần nhất (không cần job, không ghi DB), ghi vào `docs/economic-feeds-probe.md`: URL, kết quả đọc được, ngày, trang có đổi cấu trúc giữa các tháng không.
- [ ] **Step 2: Nếu** một tháng đọc không ra, sửa `CpiReleaseParser` và thêm dữ liệu mẫu của tháng đó (quay lại Task 2 Step 1).
- [ ] **Step 3: Commit** `docs(economics): CPI feed probe results`.

### Task 5: CỔNG QUYẾT ĐỊNH nguồn giá xăng (việc của Manager, kết hợp kiểm nguồn)

- [ ] **Step 1: Kiểm** các nguồn ứng viên (công văn điều hành giá của Bộ Công Thương, bảng giá Petrolimex): có đọc tự động được không, ổn định không, điều khoản sử dụng; ghi kết quả vào `docs/economic-feeds-probe.md`.
- [ ] **Step 2: Manager chọn** một: (a) tự động theo nguồn đã kiểm, (b) nhập tay hằng tuần qua trang admin (Task 6 chỉ làm bảng và API, bỏ job), (c) hoãn. Ghi quyết định vào đầu plan này.

### Task 6: Bảng chỉ báo thị trường

**Files:**
- Create: `backend/FinMate.Domain/Entities/MarketIndicator.cs`, repository và migration EF, `backend/FinMate.API/Controllers/Admin/AdminMarketIndicatorsController.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Economics/MarketIndicatorTests.cs`, test tích hợp controller

**Interfaces:**
- Produces: `MarketIndicator { Guid Id; string Key; long Value; string Unit; string SourceName; string SourceUrl; DateOnly AsOf; DateTimeOffset RecordedAt; }`, khóa duy nhất `(Key, AsOf)`; khóa đầu tiên `fuel_ron95` với `Value` là đồng/lít (`long`). API admin `GET/POST /api/v1/admin/market-indicators` (Staff ghi, mọi giá trị phải có nguồn và `AsOf` không ở tương lai); đọc `LatestAsync(key, n)` cho dự báo.

- [ ] **Step 1: Viết test**: `RejectsMissingSourceOrFutureDate`, `SameKeyAndDateIsRejectedAsDuplicate`, `LatestReturnsNewestFirst`, controller 401/403/201.
- [ ] **Step 2: Chạy** `dotnet test --filter FullyQualifiedName~MarketIndicator`. Expected: FAIL.
- [ ] **Step 3: Cài** thực thể, migration, API; bảng chỉ có dữ liệu do người/job nạp, không seed số.
- [ ] **Step 4: Chạy lại** Step 2. Expected: PASS.
- [ ] **Step 5: Commit** `feat(economics): market indicators table and admin API`.

### Task 7: Job giá xăng hằng tuần (chỉ làm nếu Task 5 chọn (a))

**Files:** `EconomicFuelRefreshJob.cs` (tương tự Task 3), bộ đọc nguồn đã chọn, test, mục cấu hình `FuelFeedUrl`.

- [ ] **Step 1–5:** như Task 3 (bộ đọc thuần có dữ liệu mẫu, job lịch thứ Năm 18:00 giờ VN, trùng `(Key, AsOf)` thì bỏ qua, lỗi thì giữ cũ và mở sự cố, ngưỡng hợp lý cấu hình). Commit `feat(economics): weekly fuel price job`.

### Task 8: Chỉ báo xăng nới dải dự báo

**Files:**
- Modify: `backend/FinMate.Application/Agent/Forecasting/GoalSimulator.cs`/công cụ `forecast_goal` (đầu vào hệ số), `AgentLimits`/config cho tham số
- Create: `backend/FinMate.Application/Economics/MarketVolatility.cs`
- Test: `backend/FinMate.Tests/Unit/Application/Economics/MarketVolatilityTests.cs`, test `forecast_goal`

**Interfaces:**
- Produces: `public static MarketState Assess(IReadOnlyList<MarketIndicator> lastWeeks, DateOnly today)` → `{ int WidenBps; bool Volatile; int MissingWeeks; }`: biến động giá 4 tuần gần nhất theo phần trăm nguyên (không `double`); `Volatile` khi vượt ngưỡng cấu hình; thiếu dữ liệu > N tuần thì `WidenBps = 0` và `MissingWeeks > 0`. `forecast_goal` cộng `WidenBps` vào tốc độ tăng giá của KỊCH BẢN BẢO THỦ, thêm bằng chứng `market_state` (chữ, nhãn "thị trường đang biến động", nguồn và ngày chỉ báo).

- [ ] **Step 1: Viết test**: `StableFuelGivesZeroWiden`, `SpikeWidensConservativeOnly`, `MissingDataGoesNeutralAndLowersConfidence`, `ForecastGoalAddsMarketStateEvidenceWhenVolatile`, `NoIndicatorsKeepsCurrentBehaviour` (hành vi cũ không đổi).
- [ ] **Step 2: Chạy** các test trên. Expected: FAIL.
- [ ] **Step 3: Cài** `MarketVolatility`, nối vào `forecast_goal`; mọi số tiền vẫn tính bằng `long`/số nguyên.
- [ ] **Step 4: Chạy lại** Step 2 và `FullyQualifiedName~Agent`. Expected: PASS.
- [ ] **Step 5: Commit** `feat(agent): market volatility widens the conservative scenario`.

### Task 9 (tùy chọn): Báo khi kết luận mục tiêu đổi

- [ ] Khi một giả định đổi bản, tìm mục tiêu dài hạn mà kết luận đổi (khả thi → cần cân nhắc) và đẩy thông báo qua cơ chế push/Insight sẵn có, có giới hạn tần suất. Chỉ làm sau khi đợt 1 chạy ổn; cần plan riêng.

### Task 10: Dữ liệu đánh giá và tài liệu

**Files:** `docs/agent-api.md` (bằng chứng `market_state`), `docs/ops-api.md` (hai job và cấu hình), `docs/agent-eval.md` (ghi rõ phần dự báo có/không chỉ báo), `ai-service/agent_eval/dataset/questions.jsonl` (≥ 8 câu hỏi dự báo dài hạn khi thị trường biến động).

- [ ] **Step 1:** cập nhật tài liệu; **Step 2:** thêm câu hỏi đánh giá và chạy `--kinds forecast` trên môi trường riêng nếu đã dựng; ghi "Chưa đo" nếu chưa.
- [ ] **Step 3: Commit** `docs(economics): economic feeds and market state`.
