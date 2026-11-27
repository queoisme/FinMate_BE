# Phase 96 — Đánh giá dự báo bằng backtest

> Trạng thái: **Tầng 1 (Task 1–9) xong 2026-11-27, dừng theo S5 chờ chủ sản phẩm quyết có sửa mô hình trước tầng 2.** Chưa push, chưa mở PR. Kết quả: `docs/evaluation/forecast-backtest.md`.
> Nhánh `feat/forecast-backtest` từ `main` @ `32912fc` (đã có Phase 95).
> Mục tiêu của phase là **đo**. Không chỉnh mô hình, không đổi tham số mặc định. Không dùng dữ liệu người dùng thật.

---

## Phần 1 — Kiểm kê

Đã đọc: `CLAUDE.md`, `AGENTS.md`, `.context/ARCHITECTURE.md`, `docs/REQUIREMENTS.md` (§6.5 FR-FCST-01…18, §11.3, §13.4), `docs/ops-api.md` (job `forecast-accuracy`, `/ops/forecast/*`, `/ops/forecast-config`), `docs/agent-eval.md`, toàn bộ `Application/Forecasting/`, `IForecastDataRepository` + `ForecastDataRepository`, `ForecastSettings` trong `SystemConfigDocument.cs`, `DemoUserSeeder`, `ai-service/agent_eval/personas.py`, `ForecastTestData` và danh sách test dự báo.

### 1. Bản đồ dự báo hiện có

Hầu hết logic đã nằm trong **hàm thuần nhận "hôm nay" làm tham số**. Phần không chạy được ở mốc quá khứ nằm ở lớp service: chỗ đọc đồng hồ, và chỗ đọc **trạng thái hiện tại** trong DB (số dư ví, khoản định kỳ, ngân sách đã chi, phản hồi bất thường).

| Loại dự báo | Hàm thuần (nhận `today`) | Service bọc ngoài | Đầu vào | Đầu ra | Tham số (`forecast.*`) | Đồng hồ ở service |
|---|---|---|---|---|---|---|
| Dòng tiền / số dư (FR-FCST-01, 02) | `CashflowEngine.Project`, `CashflowEngine.RecurringEvents`, `SpendingBaselineCalculator.Compute`, `LoanForecast.Events` | `ForecastService.ProjectAsync` | số dư ví được tính, khoản định kỳ active, giao dịch thói quen 90 ngày, tổng hạn mức ngân sách, khoản vay open, ngày lễ | số dư dự kiến/thận trọng/lạc quan từng ngày, ngày thấp nhất, ≤ 3 nguyên nhân, `confidence` | `historyWindowDays`, `minHistoryDays`, `amountTolerancePercent`, `overdueGraceDays`, `maxHorizonDays`, `holidays` (mặc định **rỗng**) | `Func<DateTimeOffset>` qua ctor `internal` ✅ |
| Số tiền có thể chi (FR-FCST-05, 06) | `SpendableCalculator.Compute` (`SpendableInput.Today`), `IncomeEstimator.Irregular` | `ForecastService.GetSpendableAsync` | như trên + mục tiêu, dự phòng | số có thể chi, mỗi ngày, bảng cách tính | như trên + `reservePercentOfIncome`, `irregularIncomeMonths` | ✅ |
| Thu nhập (FR-FCST-13) | `IncomeEstimator.Upcoming / Late / Irregular` | `IncomeForecastService` | khoản thu định kỳ, giao dịch ~7 tháng | lịch thu 60 ngày, khoản trễ, thu không đều (P25 / trung bình) | `incomeLateDays`, `irregularIncomeMonths` | ✅ |
| Rủi ro ngân sách (FR-FCST-11) | `BudgetRiskCalculator.Compute`, `.ShouldWarn` | `BudgetForecastService` | ngân sách + `budget_periods` kỳ hiện tại, giao dịch 90 ngày, khoản định kỳ | dự kiến cuối kỳ, `ok/at_risk/over`, mức tối đa/ngày | `budgetRiskMinElapsedDays`, `budgetRiskMinDaysRemaining` | ✅ (nhưng kỳ luôn là tháng của `now`) |
| Chi theo danh mục tháng tới (FR-FCST-15) | `CategoryForecastCalculator.Compute(today, targetMonth)` | `CategoryForecastService` | giao dịch 3 tháng trọn, hạn mức danh mục, id khoản bất thường chưa xác nhận | dự kiến từng danh mục, % đổi, hạn mức đề xuất | `categoryForecastMonths`, `categoryForecastMinMonths`, `categoryChangePercent`, `budgetSuggestionRoundingCents` | ❌ `VietnamTime.Today()`, `DateTimeOffset.UtcNow` trực tiếp |
| Mục tiêu tiết kiệm (FR-FCST-16) | `GoalForecastCalculator.Compute(…, today, …)` | `GoalForecastService` | mục tiêu (`SavedCents`), khoản góp theo tháng | ngày đạt trung bình/thận trọng, trạng thái | `goalRateMonths` | ❌ `VietnamTime.Today()` |
| Phát hiện khoản định kỳ (FR-FCST-09a) | `RecurringDetector.Detect(history, knownKeys, today, rules)` | `RecurringDetectionService` (job 01:30) | giao dịch 90 ngày + chuyển khoản ra khỏi tập ví được tính | khoản `suggested`/`active` mới | `suggestMinOccurrences`, `autoActivateOccurrences`, khoảng chu kỳ tuần/tháng, dung sai | ✅ |
| Theo dõi khoản định kỳ, đổi giá (FR-FCST-12, 18) | `RecurringTracker.Advance(item, history, rules, now, …)` — **sửa entity tại chỗ**; `RecurringMatcher`; `RecurringSchedule`; `RecurringCompletion.Apply` | `RecurringDetectionService` | entity `RecurringItem` + giao dịch | đẩy `NextDueDate`, cập nhật số tiền, `PriceChange`, liên kết thanh toán | như trên | ✅ |
| Bất thường (FR-FCST-14) | `AnomalyDetector.Evaluate(candidate, history, items, priorNormals, …)` | `AnomalyService.ScanAsync` | khoản chi 3 ngày gần nhất, lịch sử 90 ngày, phản hồi "bình thường" trước đây | `duplicate / merchant_spike / category_spike`, mức `normal/high` | `anomaly*`, `duplicateWindowMinutes`, `normalFeedbackTolerancePercent`, `anomalyScanDays` | ❌ `VietnamTime.Today()` |
| Cảnh báo số dư thấp (FR-FCST-10) | `LowBalanceRule.Evaluate(forecast, threshold)` | `ForecastAlertService.CheckLowBalanceAsync` → `ForecastAlertDispatcher` | dự báo `lowBalanceHorizonDays` ngày, ngưỡng của user | ngày thiếu, số dư lúc đó, chi tối đa/ngày | `lowBalanceHorizonDays`, `lowBalanceCooldownDays`, `inaccurateSuppressDays`, giờ yên lặng | ❌ `VietnamTime.Today()` ở service; dispatcher có `Func<DateTimeOffset>` |
| Cảnh báo rủi ro ngân sách | `BudgetRiskCalculator.ShouldWarn` | `ForecastAlertService.CheckBudgetsAsync` | kết quả `BudgetForecastService` | một cảnh báo/ngân sách/tháng | `budgetRiskMinDaysRemaining` | ❌ |
| Nhắc hạn / quá hạn / tạm ngừng hóa đơn | `BillReminderRule.Evaluate(items, recent, today, daysBefore, rules)` | `ForecastAlertService.RemindBillsAsync` | khoản chi định kỳ + giao dịch gần đây | `DueSoon / Overdue / LongOverdue` (LongOverdue **ghi DB**: chuyển `paused`) | `billReminderDaysBefore`, `overdueGraceDays` | ❌ |
| Thu nhập trễ | `IncomeEstimator.Late` | `ForecastAlertService.WarnLateIncomeAsync` | khoản thu định kỳ + giao dịch | `LateIncomeDto` | `incomeLateDays` | ❌ |
| Cảnh báo bất thường mức cao | (kết quả `AnomalyService`) | `ForecastAlertService.CheckAnomaliesAsync` | | một cảnh báo/giao dịch | | ❌ |

Ngoài phạm vi phase này (ghi để khỏi bỏ sót): `PurchaseCheckService`/`PurchaseVerdict` (gọi lại engine dòng tiền hai lần, không có "đáp án" để chấm), `WeeklyDigestService` (tóm tắt, không dự báo), `Agent/Forecasting/IntervalForecaster` + `GoalSimulator` (đã có bộ đo riêng `ai-service/agent_eval`, dữ liệu tháng).

#### Chỗ chưa chạy được ở một mốc quá khứ

| # | Chỗ | Vì sao rò rỉ / sai mốc | Nặng nhẹ với dữ liệu tổng hợp |
|---|---|---|---|
| L1 | `ForecastService`: `accounts.Sum(a => a.BalanceCents)` | Số dư **hiện tại**, không phải số dư tại mốc. | Phải dựng lại: số dư mở đầu + dòng tiền thô (`GetRawTransactionsAsync`, gồm vay) ≤ mốc + chuyển khoản vượt ranh giới tập ví được tính |
| L2 | Mọi service: `IRecurringItemRepository.ListAsync(Active)` | Trạng thái **hiện tại**: `NextDueDate`, `AmountCents` (tracker đã cập nhật theo giá mới), `Status` (có thể đã `paused`/`completed`), `LivePaymentCount`. Khoản được phát hiện sau mốc vẫn hiện ra. | Phải có trạng thái tại mốc: hoặc "đáp án" (khoản người dùng khai báo), hoặc **phát lại** `RecurringDetector` + `RecurringTracker` từng ngày trên dữ liệu ≤ mốc |
| L3 | `BudgetForecastService`: `budget_periods.SpentCents`, kỳ = tháng của `now` | Số đã chi là tổng **đến bây giờ** (bảng tổng hợp). | Tính lại từ giao dịch ≤ mốc qua `TransactionBudgetDelta`/`BudgetSpendScope` (không viết lại luật) |
| L4 | `ILoanRepository.ListOpenAsync` | Dư nợ và trạng thái khoản vay hiện tại. | Bộ sinh tự giữ sổ vay theo thời gian |
| L5 | `IAnomalyRepository.GetPriorNormalsAsync`, `transaction_anomalies`, `excludedIds` của dự báo danh mục | Phản hồi của người dùng đưa ra **sau** mốc. | Dữ liệu tổng hợp không có phản hồi (giả định rõ trong báo cáo), hoặc mô phỏng phản hồi có mốc thời gian |
| L6 | Cờ trên giao dịch: `ExcludedFromForecast`, `IsReversed`, `DuplicateOfTransactionId`, trạng thái `Confirmed`, danh mục đã sửa, dòng đã gộp | Là trạng thái **bây giờ**: người dùng có thể đánh dấu/xác nhận/gộp sau mốc. | Chỉ là vấn đề với dữ liệu thật. Bộ sinh đặt các cờ này ngay lúc tạo |
| L7 | `RefundNetting.ForHabits` | Thuần, chỉ trừ hoàn tiền nằm **trong** tập dòng truyền vào. Không rò rỉ nếu lọc `≤ mốc` **trước** khi trừ. Repository hiện lọc `[from, to]` rồi mới trừ: đúng. | Không |
| L8 | `ForecastService.BaselineAsync`: cache Redis theo `(user, today)` | Không rò rỉ, nhưng cache giả phải theo cùng khóa. | Không |
| L9 | `VietnamTime.Today()` / `DateTimeOffset.UtcNow` gọi thẳng | `AnomalyService`, `CategoryForecastService`, `GoalForecastService`, `ForecastAlertService`, `ForecastAccuracyService`. | Cần thêm ctor `internal` nhận `Func<DateTimeOffset>` (như `ForecastService` đã có), hoặc gọi thẳng hàm thuần |
| L10 | `GetTransactionsAsync(…, to: today)` lấy cả giao dịch **của chính hôm nay** | Engine trừ phần đã chi hôm nay (`SpentTodayFlexibleCents`). | Phải chốt ngữ nghĩa mốc: đề xuất mốc = **đầu ngày T** (giống job chụp 02:30), chỉ thấy giao dịch `Date < T` |
| L11 | `Guid.NewGuid()` trong `ForecastSnapshotBuilder` và fixture test | Không rò rỉ, nhưng phá tính lặp lại nếu id lọt vào thứ tự sắp xếp. | Bộ sinh dùng id tất định |

Không có chỗ nào trong **hàm thuần** đọc đồng hồ hay DB. Một điểm tinh tế: `SpendingBaselineCalculator.Compute` khi chưa đủ lịch sử dùng `DateTime.DaysInMonth(today…)` — đúng mốc, không rò rỉ.

### 2. Độ chính xác hiện có (`ForecastAccuracy*`)

| | Hiện có |
|---|---|
| Đo gì | Chỉ **số dư dòng tiền** (tổng các ví được tính) ở tầm 7, 14, 30 ngày. |
| Cách lấy dự báo | Job `forecast-accuracy` 02:30 VN: `ForecastService.GetCashflowAsync(user, null, today+30)` cho mọi người có hoạt động trong 90 ngày; `ForecastSnapshotBuilder.Build` chụp mỗi tầm một dòng: dự kiến, thận trọng, lạc quan, mốc B0 "chỉ khoản định kỳ" (tính từ chính các sự kiện của dự báo), `confidence`, `config_version`, `method = median_p25p75`. |
| Cách lấy thực tế | `ForecastActuals.NetFlow`: số dư lúc chụp + thu chi thô (có vay, có giao dịch bị loại khỏi dự báo, cộng chuyển khoản vượt ranh giới tập ví được tính, Phase 83 S4) từ ngày gốc tới ngày của tầm. |
| Chỉ số | `ForecastAccuracyCalculator.Compute`: MAE, trung vị sai số tuyệt đối, MAE của B0 và của "số dư giữ nguyên", % thực tế nằm trong dải, độ rộng dải trung bình, tỉ số MAE/MAE_giữ-nguyên và MAE/MAE_B0; tách `all/low/normal`. |
| Lưu | Bảng `forecast_snapshots`; phản hồi "dự báo sai" ở `forecast_feedback` (Staff gán nguyên nhân). |
| Ai xem | `GET /api/v1/ops/forecast/accuracy?windowDays=` — OpsManager trở lên (`ViewMonitoring`), chỉ số gộp, không có id người dùng. |

**Khác backtest ở chỗ:** đo **tiến** trên người dùng thật, nên phải chờ 30 ngày mới có số, không lặp lại được (dữ liệu thay đổi), không có đáp án cho khoản định kỳ hay bất thường, không tách theo kịch bản, chưa có bias/WAPE, chưa đo cảnh báo, thu nhập, ngân sách, danh mục. Đồ án hiện **chưa có người dùng thật**, nên bảng này gần như trống trước buổi bảo vệ.

**Tái dùng được:**
- `ForecastSnapshotBuilder` (tầm 7/14/30, cách tính B0) và `ForecastAccuracyCalculator` nhận `ForecastSnapshot` trong bộ nhớ, không cần DB: backtest chỉ cần dựng các bản chụp ảo rồi gọi lại.
- `ForecastActuals.NetFlow` làm định nghĩa "thực tế" chung: production và backtest đo cùng một thứ.
- `Statistics` (`Median`, `Quantile`, `TrimUpperOutliers`).

**Một điều backtest sẽ làm lộ ra (ghi trước, không sửa trong phase này):** chú thích của `CoveragePercent` nói dải P25–P75 đúng nghĩa thì phủ gần 50%. Nhưng dải của engine là **tổng các phân vị theo ngày**, không phải phân vị của tổng. Tổng các P75 lớn hơn P75 của tổng, nên dải sẽ rộng dần theo tầm và tỉ lệ phủ sẽ vượt xa 50% ở tầm 30 ngày. Backtest sẽ báo con số này thay vì giả định.

### 3. Dữ liệu có thể dùng

| Nguồn | Số người | Lịch sử | Định kỳ | Thu không đều | Lễ/Tết | Bất thường | Vay | Quỹ nhóm | Hoàn tiền | Chuyển ví | Dùng được? |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `DemoUserSeeder` (DEV) | 1 | 6 tháng, tất định | 2 (trọ, lương) + 2 không khai báo (internet, mua sắm ngày 20) | Không | Không | Không | Không | Không | Không | Không (1 ví) | Không: quá đều. Mô hình dự báo nó gần như hoàn hảo, đúng kiểu "tự chấm bài" |
| `ai-service/agent_eval/personas.py` | 3 kiểu × hạt giống | 12 tháng + 24 tháng "tương lai" để chấm | Học phí theo kỳ (sinh viên) | Có (freelancer) | Không | Không | Không | Không | Không | Không | Chỉ là **tổng theo tháng**, không có giao dịch. Mượn lại **dải tham số** (lương, tỉ lệ chi, độ dao động) để hồ sơ nhất quán giữa hai bộ đo |
| `ForecastTestData` + unit test | — | vài giao dịch mỗi test, ngày cố định 2026-11-02 | | | | | | | | | Dùng lại các hàm dựng (`Debit`, `Credit`, `Item`), không phải bộ dữ liệu |
| Integration test (`ForecastAccuracyJobTests`, `RefundForecastTests`, `ForecastLoanTests`, `ForecastBoundaryTransferTests`…) | — | kịch bản nhỏ | | | | | có | | có | có | Là nguồn **luật loại trừ** để bộ sinh làm đúng (Q16), không phải dữ liệu |
| Bộ dữ liệu công khai (Kaggle…) | | | | | | | | | | | Không đề xuất: không theo mẫu chi tiêu Việt Nam, giấy phép không rõ, phải thêm phụ thuộc |

**Đề xuất:** bộ sinh **dữ liệu tổng hợp có kiểm soát**, viết bằng C#, chạy trong bộ nhớ.

- **Tất định:** PRNG tự viết (SplitMix64, ~20 dòng) thay vì `System.Random`, để kết quả không phụ thuộc phiên bản .NET. Id giao dịch tất định (`DeterministicGuid.V5` đã có trong dự án). Cùng hạt giống → cùng dữ liệu, từng đồng.
- **Hồ sơ (persona):** `stable_worker` (lương cố định), `freelancer` (thu không đều), `student` (trợ cấp + làm thêm + học phí theo kỳ), `family` (2–3 ví, một ví bị loại khỏi dự báo, chuyển ví, vay, quỹ nhóm, hoàn tiền), `new_user` (14–45 ngày lịch sử). Mỗi hồ sơ × N hạt giống (đề xuất 20) × 18 tháng (6 tháng khởi động + 12 tháng đánh giá).
- **Có đáp án:** bộ sinh ghi lại danh sách khoản định kỳ thật (người nhận, chiều, chu kỳ, số tiền theo từng kỳ, kỳ bị bỏ, ngày đổi giá) và danh sách giao dịch bất thường được cài (loại, mức). Các nhãn này không bao giờ đi vào mô hình.
- **Bộ sinh giả định gì (sẽ ghi nguyên văn vào báo cáo):** chi linh hoạt theo danh mục, số lần mỗi ngày theo Poisson, số tiền theo log-normal (đuôi nặng); có hiệu ứng thứ trong tuần; có **hiệu ứng ngày lương** (chi tăng vài ngày sau khi nhận lương — mô hình không biết); có **trôi mức chi** (tăng/giảm dần vài %/tháng — mô hình chỉ nhìn 90 ngày); ngày trả định kỳ lệch ±1–3 ngày, số tiền hóa đơn điện nước dao động; tên nơi chi viết vài kiểu khác nhau (thử `CounterpartyKey`). Mọi số tiền là `long` VND, làm tròn nghìn; ngày giờ theo UTC+7.
- **Luật loại trừ thống nhất với Q16:** vay/trả nợ, góp/rút quỹ nhóm, chuyển giữa ví của mình đi vào **dòng tiền thô** (số dư thực tế) nhưng không vào chi tiêu/thu nhập. Bộ sinh tạo đúng loại giao dịch production lưu (`Transfer` không ví đối ứng cho quỹ, `loan` slug, `relation = refund`…) và đi qua cùng `RefundNetting` / bộ lọc `IsLoan` như repository.

**Kịch bản mô hình không biết trước** (cài vào theo lịch, mỗi kịch bản có nhãn thời gian để chấm riêng):

| Mã | Kịch bản | Cách cài | Kiểm cái gì |
|---|---|---|---|
| K1 | Chi đột xuất lớn | 1–2 khoản 3–10 triệu/năm, không báo trước | Số dư sau sự kiện, cảnh báo số dư thấp, phát hiện bất thường |
| K2 | Thu nhập về trễ / đổi số tiền | Lương trễ 3–10 ngày; tăng/giảm 10–30% từ một tháng | Dự báo số dư quanh ngày lương, cảnh báo thu nhập trễ |
| K3 | Bỏ một kỳ định kỳ | Một tháng không trả (hoặc trả gộp tháng sau) | Tạm ngừng nhầm, trừ hai lần, nhắc hạn |
| K4 | Khoản định kỳ đổi giá | +5% (trong dung sai) và +25% (ngoài dung sai) | Phát hiện đổi giá, độ trễ cập nhật |
| K5 | Tháng Tết | Chi linh hoạt ×1,5–2,5 trong 2–3 tuần quanh Tết; chạy hai lần: `holidays` rỗng (mặc định) và có cấu hình | Dự báo khi mô hình không biết vs khi admin đã khai |
| K6 | Người dùng mới | 14–45 ngày lịch sử | Nhánh `confidence = low` (ngân sách / trung bình / 0) |
| K7 | Trùng giao dịch | Cùng ví, số tiền, nơi nhận trong vài phút | Bất thường `duplicate` |
| K8 | Đổi thói quen | Mức chi đổi bậc ±30% từ một ngày | Thời gian để dự báo bắt kịp |
| K9 | Định kỳ mô hình không hỗ trợ | Chu kỳ 2 tuần, hằng quý | Không phát hiện nhầm thành tuần/tháng; số dư bị ảnh hưởng ra sao |
| K0 | Đối chứng | Không cài gì | Mốc để so các kịch bản trên |

Sáu kịch bản K1–K6 là yêu cầu của đề bài; K7–K9 và K0 là đề xuất thêm.

**Chống tự chấm bài:** đặc tả bộ sinh viết và chốt (có số phiên bản) **trước khi** chạy lần đầu; không sửa bộ sinh sau khi đã thấy kết quả để số đẹp hơn. Nếu phải sửa (lỗi thật), tăng phiên bản và ghi lý do trong báo cáo.

### 4. Chỉ số đề xuất

Tầm 7/14/30 ngày cho mọi dự báo theo ngày. Mọi bảng tách theo **kịch bản**, **hồ sơ**, **`confidence`** (low/normal). Khoảng tin cậy 95% bằng bootstrap theo người (hạt giống cố định).

| Dự báo | Chỉ số |
|---|---|
| Số dư (dòng tiền) | MAE (đ), trung vị sai số tuyệt đối, **bias** (trung bình dự kiến − thực tế; âm = dự báo bi quan), **sai số tương đối theo quy mô**: MAE / trung bình \|dòng tiền thực tế trong tầm\| (thay MAPE, vì số dư gần 0 hoặc âm làm MAPE vô nghĩa), tỉ số với từng baseline, % thực tế trong dải thận trọng–lạc quan, độ rộng dải. Thêm: sai số số dư thấp nhất trong kỳ và lệch ngày thấp nhất (ngày). |
| Chi linh hoạt (tách riêng khỏi định kỳ) | Tổng chi linh hoạt dự kiến trong tầm vs thực tế: **WAPE** = Σ\|F−A\| / ΣA (gộp trên mọi mốc nên không bị mẫu số gần 0), bias; MAPE chỉ trên mốc có A ≥ 100.000đ, kèm số mốc bị loại. |
| Thu nhập | Định kỳ: lệch ngày nhận dự kiến (ngày), sai số số tiền. Không đều: % tháng thực tế ≥ mức thận trọng (P25 → kỳ vọng ≈ 75%), WAPE của mức trung bình. |
| Rủi ro ngân sách | Theo ngày trong tháng (nhóm 1–7, 8–15, 16–23, 24+): WAPE và bias của chi cuối kỳ dự kiến; phân loại `at_risk` so với vượt thật: precision, recall, F1. |
| Danh mục tháng tới | WAPE theo danh mục và tổng, bias, độ đúng của cờ "thay đổi nhiều". |
| Phát hiện khoản định kỳ | So với đáp án: precision, recall (theo người nhận + chiều), đúng chu kỳ, sai số số tiền, **thời gian tới khi phát hiện** (ngày từ lần trả đầu), lệch `NextDueDate`. Đổi giá: precision/recall, độ trễ. Tạm ngừng nhầm (K3). |
| Bất thường | So với giao dịch được cài: precision, recall theo loại; precision của mức `high`; **số cờ sai mỗi người-tháng** (gánh nặng thông báo). |
| Cảnh báo | Sự kiện thật = số dư thực tế < ngưỡng trong tầm `lowBalanceHorizonDays`; rủi ro ngân sách = vượt thật; thu nhập trễ = trễ được cài. Mỗi loại: precision, recall, **thời gian báo trước** (ngày từ cảnh báo tới sự kiện), số báo nhầm mỗi người-tháng. Áp chống lặp theo `dedup_key` như production (mỗi đợt một lần), không áp giờ yên lặng (không ảnh hưởng đúng/sai). |
| Mục tiêu tiết kiệm | **Không đề xuất đo**: khoản góp trong dữ liệu tổng hợp do bộ sinh tự đặt, đo nó là tự chấm bài thuần túy. Ghi vào hạn chế. |

### 5. Mốc tham chiếu (baseline)

| Mã | Cách tính | Dùng cho |
|---|---|---|
| B-hold | Số dư giữ nguyên (đã có) | Số dư |
| B0 | Chỉ khoản định kỳ, không có chi linh hoạt (đã có) | Số dư |
| B-mean30 | Khoản định kỳ như mô hình + chi linh hoạt = **trung bình** 30 ngày trước, mọi ngày như nhau | Số dư, chi linh hoạt. Tách được giá trị của trung vị theo thứ + cắt ngoại lai |
| B-naive | Lặp lại dòng tiền ròng của h ngày ngay trước mốc | Số dư (mốc "ngây thơ" chuẩn trong tài liệu dự báo chuỗi thời gian) |
| B-lastmonth | Tháng này = tháng trước (tổng và theo danh mục) | Rủi ro ngân sách, danh mục tháng tới |

Ghi chú: nhịp chi "đã chi / ngày đã qua × số ngày" **không** làm baseline cho ngân sách được, vì đó chính là nhánh `pace` của mô hình.

**So sánh công bằng:** cùng mốc, cùng dữ liệu nhìn thấy (≤ đầu ngày T), cùng luật loại trừ (Q16), cùng tầm, cùng định nghĩa thực tế (`ForecastActuals.NetFlow`). Baseline không có tham số nào được chỉnh trên dữ liệu đánh giá. Báo **chênh lệch cặp** theo từng mốc (mô hình − baseline) với khoảng bootstrap, không chỉ hai con số trung bình.

### 6. Hình thức chạy và đầu ra

**Đề xuất:** một project console mới `backend/FinMate.Backtest` (tham chiếu `FinMate.Application`, **không thêm NuGet**), cộng test xUnit trong `FinMate.Tests`.

- Lệnh: `dotnet run --project FinMate.Backtest -- --seed 42 --out ../docs/evaluation/` (chạy trong container SDK 9 như các lệnh khác; không cần Postgres, Redis hay Docker compose).
- Cách gọi mô hình: chạy **chính** `ForecastService` (và các service khác) với repository giả trong bộ nhớ và đồng hồ giả qua ctor `internal` đã có. Ưu điểm: dùng đúng phần ghép nối của production (`ProjectAsync`: chọn ví, lọc khoản định kỳ, loại trừ D5, chuyển khoản vượt ranh giới, vay), không chép lại logic. Nơi nào service đọc đồng hồ trực tiếp (L9) thì thêm ctor `internal` nhận `Func<DateTimeOffset>`, giữ ctor công khai như cũ.
- Các ctor đồng hồ và `TransactionBudgetDelta` (dùng để tính lại số đã chi, L3) đều là `internal`; hiện `FinMate.Application.csproj` chỉ mở cho `FinMate.Tests`. Project console cần thêm một dòng `InternalsVisibleTo Include="FinMate.Backtest"`; nếu không muốn mở thêm thì runner nằm trong `FinMate.Tests` và console chỉ là lớp vỏ gọi nó.
- Test xUnit: tất định (hai lần chạy cùng hạt giống → CSV giống từng byte), **không rò rỉ** (thêm giao dịch sau mốc không đổi kết quả tại mốc), hồi quy production (kết quả service với ctor cũ không đổi), một lượt chạy nhỏ làm khói.
- Đầu ra: `docs/evaluation/forecast-backtest.md` (bảng tóm tắt cho người không chuyên ở đầu, rồi bảng theo kịch bản, giả định của bộ sinh, hạn chế) + `docs/evaluation/forecast-backtest/*.csv` (một file mỗi nhóm chỉ số, dòng = kịch bản × hồ sơ × tầm × baseline). Báo cáo ghi: commit, phiên bản bộ sinh, hạt giống, phiên bản/nội dung tham số `forecast`.
- API cho OpsManager: **không cần** ở tầng bắt buộc (xem mục 7).
- **Ước lượng thời gian chạy:** 5 hồ sơ × 20 hạt giống = 100 người; mốc mỗi ngày trong 12 tháng đánh giá ≈ 36.500 mốc cho dòng tiền. Mỗi mốc: baseline trên ~90 ngày × vài giao dịch + chiếu 30 ngày, cỡ < 1 ms. Phát lại khoản định kỳ và quét bất thường mỗi ngày tốn nhất (bất thường so mỗi khoản chi với lịch sử 90 ngày). Ước tính **dưới 1 phút** cho dòng tiền, **1–3 phút** cho cả bộ. Nếu chậm hơn thì giảm mốc dòng tiền xuống mỗi 3 ngày (vẫn > 12.000 mốc).

### 7. Liên kết Phase 95 (chạy thử bản nháp tham số)

Mọi bộ tính đều nhận tham số qua `ForecastRules` / `AnomalyRules` / `CategoryForecastRules` / `ForecastSettings`, nên chạy cùng bộ dữ liệu hai lần (tham số đang chạy vs bản nháp) chỉ là đổi đầu vào. Lưu ý: phần `limits` của bản nháp (T1/T2 mặc định) không vào bộ tính dự báo nào; nó dùng cho `BudgetAlertEvaluator` (cảnh báo ngưỡng 70/90), không thuộc phase này.

| Cách | Làm gì | Chi phí | Rủi ro |
|---|---|---|---|
| (a) CLI nhận file tham số | `--config active.json --compare draft.json` → thêm cột "bản nháp" và chênh lệch vào báo cáo | ~1 task | Thấp. Người duyệt phải chạy tay hoặc nhờ dev |
| (b) API `POST /ops/forecast-config/drafts/{id}/trial` | Chạy một bộ hồ sơ rút gọn (vd. 5 hồ sơ × 2 hạt giống, chỉ dòng tiền + cảnh báo) đồng bộ, trả bảng so sánh; cần quyền, nhật ký, giới hạn thời gian (≤ 5 s) | ~3–4 task, phải chuyển bộ sinh + runner vào `FinMate.Application` | Trung bình: dữ liệu tổng hợp có thể làm người duyệt tin quá mức; phải ghi rõ trên kết quả; thêm mã vào lớp Application |

### 8. Đề xuất phạm vi

| Phương án | Gồm | Số task (TDD, một commit mỗi task) | Rủi ro chính |
|---|---|---|---|
| **A — Dòng tiền + baseline** | Bộ sinh (5 hồ sơ, K0–K6), dựng trạng thái tại mốc (số dư dựng lại; khoản định kỳ theo **đáp án**), chạy `ForecastService` với đồ giả, chỉ số số dư + chi linh hoạt, 4 baseline (B-hold, B0, B-mean30, B-naive), báo cáo MD + CSV, test tất định / không rò rỉ / hồi quy, REQUIREMENTS + TASKS | ~8 | Khoản định kỳ theo đáp án làm số đẹp hơn thực tế (mô hình được biết trước khoản định kỳ). Phải ghi rõ; phần phát lại để phương án B |
| **B — A + cảnh báo, định kỳ, bất thường, thu nhập, ngân sách, danh mục** | Thêm: phát lại `RecurringDetector` + `RecurringTracker` từng ngày (chế độ "phát lại" bên cạnh "đáp án"), K7–K9, chỉ số phát hiện định kỳ/đổi giá/tạm ngừng, bất thường, thu nhập, rủi ro ngân sách + B-lastmonth, danh mục tháng tới, 4 loại cảnh báo (số dư thấp, rủi ro ngân sách, thu nhập trễ, nhắc/tạm ngừng hóa đơn) có chống lặp. Ctor `internal` có đồng hồ cho `AnomalyService`, `CategoryForecastService`, `ForecastAlertService` (hoặc gọi thẳng hàm thuần) | ~14 | Phát lại tracker phải dùng đúng `RecurringTracker` / `RecurringCompletion` trên entity trong bộ nhớ (sửa tại chỗ), dễ lệch production nếu bỏ sót bước; thời gian chạy; nhiều bảng hơn để đọc |
| **C — B + chạy thử bản nháp Phase 95** | (a) CLI so hai bộ tham số: +1 task (≈ 15). Hoặc (b) API cho OpsManager: +3–4 task (≈ 18) | 15 hoặc 18 | (b): xem mục 7 |

**Đề xuất của tôi: B + C(a).** A một mình chưa trả lời được phần cảnh báo, mà đó là phần hội đồng thấy rõ nhất trong app. Thêm CLI so hai bộ tham số gần như không tốn gì khi runner đã nhận tham số. Để API (C-b) cho sau, khi có số liệu chứng minh bộ dữ liệu tổng hợp đủ tin để dùng cho quyết định duyệt.

Các tầng trong bước 2 sẽ chia sao cho dừng ở đâu cũng an toàn: tầng 1 = A (đủ để có số cho tài liệu "AI Dataset and Evaluation"), tầng 2 = phần B, tầng 3 = C(a).

### Câu hỏi cần chốt cùng phạm vi

1. **Phạm vi:** A, B, B + C(a) hay B + C(b)?
2. **Nơi chạy:** project console `FinMate.Backtest` (đề xuất) hay chỉ test xUnit ghi file? Console tách rõ "đo" khỏi "kiểm thử" và không làm chậm `dotnet test`.
3. **Ngữ nghĩa mốc:** đầu ngày T, chỉ thấy giao dịch `Date < T` (đề xuất, giống job chụp 02:30)?
4. **Cỡ mẫu:** 5 hồ sơ × 20 hạt giống × 12 tháng đánh giá, mốc mỗi ngày (đề xuất)?
5. **Nơi để báo cáo:** `docs/evaluation/forecast-backtest.md` + `docs/evaluation/forecast-backtest/*.csv`? (`docs/evaluation.md` hiện là một file đơn cho chat/OCR; đề xuất tạo thư mục `docs/evaluation/` và để file cũ nguyên chỗ.)

---

## Đã chốt (2026-11-27): phạm vi B + C(a), bốn tầng

1. Tầng 1 = phương án A. Tầng 2 = chế độ phát lại song song chế độ đáp án, phát hiện định kỳ / đổi giá / tạm ngừng nhầm, cảnh báo số dư thấp, bất thường, K7–K9. Tầng 3 = thu nhập, rủi ro ngân sách + B-lastmonth, danh mục tháng tới, các cảnh báo còn lại. Tầng 4 = CLI `--config` / `--compare`. Xong mỗi tầng: test xanh, báo cáo chạy được, là điểm dừng an toàn.
2. Console `FinMate.Backtest` + test xUnit, không thêm NuGet, build được trong solution.
3. Mốc = đầu ngày T giờ VN, chỉ thấy giao dịch có ngày < T.
4. 5 hồ sơ × 20 hạt giống × 12 tháng đánh giá, mốc mỗi ngày; chậm quá thì mỗi 3 ngày, ghi vào báo cáo.
5. Báo cáo ở `docs/evaluation/forecast-backtest.md` + `docs/evaluation/forecast-backtest/*.csv`; `docs/evaluation.md` giữ nguyên.
6. Đặc tả bộ sinh có số phiên bản, chốt bằng **một commit riêng trước commit runner đầu tiên**. K5 dùng ngày Tết âm lịch thật, ghi nguồn.
7. Dải P25–P75: chỉ đo tỉ lệ phủ và độ rộng theo tầm, không sửa; ghi vào §13.4.
8. Production chỉ được thêm ctor `internal` nhận đồng hồ; ctor công khai và kết quả giữ nguyên, có test hồi quy.
9. Bảng tóm tắt đầu báo cáo, mỗi dòng ghi chế độ (đáp án / phát lại).

## Đã duyệt phần 2 (2026-11-27): D1–D21 kèm S1–S5

- **S1 (tầm nhìn):** repository giả cắt theo **thời điểm của đồng hồ giả** (`TransactedAt < now`), không theo ngày. Mốc dự báo T 00:00 vẫn tương đương "ngày < T". Có test: lượt 08:00 của ngày D chỉ thấy giao dịch trước D 08:00.
- **S2 (D15):** production **không** có ngưỡng số dư thấp mặc định (`users.low_balance_threshold_cents` null = tắt, không có giá trị trong `forecast.*`), nên dùng 20% thu nhập tháng danh nghĩa như D15. Ghi trong đặc tả và báo cáo.
- **S3:** mọi precision / recall / tỉ lệ in kèm **n** và CI 95%; n < 20 → "chưa đủ mẫu". Cảnh báo phát ra khi số dư thực tế **đã** dưới ngưỡng là nhóm riêng "đang thiếu", không vào đúng/sai.
- **S4 (tất định):** "giống từng byte" cam kết trên **cùng image SDK**; báo cáo ghi phiên bản SDK/runtime. Số tiền làm tròn tới nghìn đồng ngay sau mỗi phép biến đổi phân phối.
- **S5:** dừng sau tầng 1. Xong Task 9: chạy đầy đủ, gửi bảng tóm tắt + bảng dải P25–P75 theo tầm, rồi dừng; chủ sản phẩm quyết có sửa mô hình (phase riêng) trước khi làm tầng 2.
- **D20:** duyệt thêm mục ngắn §2.x "Backtest dự báo" vào `.context/ARCHITECTURE.md`.
- **Mốc test:** `main` @ `32912fc` = 4293/4295; hai test đỏ có sẵn (`MethodSelectionOnSeedDataTests.TheExampleProfileInTheMdFile_GetsCashFlowBudget…`, `NotificationsControllerTests.Analyze_HighConfidence…AutoCommits…`) không tính là hồi quy.

## Phần 2 — Thiết kế

### Phát hiện thêm khi soạn phần 2

- `backend/Dockerfile` chép **từng** `.csproj` rồi chạy `dotnet restore FinMate.sln`. Thêm project vào solution mà không thêm dòng `COPY FinMate.Backtest/FinMate.Backtest.csproj` thì build Docker hỏng ở bước restore. Tính vào D1.
- Repo không có workflow CI nào (không có `.github/`). "Build được trong CI" ở đây hiểu là: `dotnet build FinMate.sln` + `dotnet test` trong container SDK 9, và `docker build backend/` đều qua.
- `FinMate.Tests` dùng Moq, nhưng project console không được thêm NuGet. Vì vậy repository giả trong `FinMate.Backtest` phải **viết tay** (D7). Tổng cộng khoảng 75 phương thức trên 9 interface, phần lớn chỉ ném lỗi.

### Quyết định

**D1. Cấu trúc project.** Đề xuất:
- `backend/FinMate.Backtest/` là console, `OutputType=Exe`, framework lấy từ `Directory.Build.props`. Tham chiếu `FinMate.Application` + `FinMate.Domain`, **không** tham chiếu `FinMate.Infrastructure` (không DB, không Redis).
- Thư mục con: `Generation/` (PRNG, hồ sơ, kịch bản, đáp án), `Simulation/` (trạng thái tại mốc, repository giả, đồng hồ, phát lại), `Evaluation/` (baseline, chỉ số, bootstrap), `Reporting/` (Markdown, CSV), `Program.cs` (đọc tham số dòng lệnh).
- `FinMate.Tests` tham chiếu `FinMate.Backtest`.
- `FinMate.Application.csproj` thêm `<InternalsVisibleTo Include="FinMate.Backtest" />`.
- `FinMate.sln` thêm project; `Dockerfile` thêm một dòng `COPY`. Image publish chỉ `FinMate.API` nên không phình.

**D2. Đặc tả bộ sinh.** Đề xuất: `docs/evaluation/forecast-backtest-generator-spec.md`, phiên bản **1.0**; hằng `GeneratorSpec.Version = "1.0"` trong code được ghi vào báo cáo.
- Đặc tả phủ **cả bốn tầng** (5 hồ sơ, K0–K9, ngưỡng số dư thấp, hạn mức ngân sách, mục tiêu đánh giá), để tầng 2–3 không phải mở lại.
- Commit riêng, **trước mọi code** của bộ sinh và runner.
- Luật sửa: chỉ sửa khi có lỗi thật (bộ sinh làm khác đặc tả, hoặc tạo dữ liệu production không thể có). Mỗi lần sửa tăng phiên bản (1.1…), ghi lý do vào mục "Lịch sử phiên bản" của đặc tả, và báo cáo in rõ phiên bản. Không sửa vì số xấu.

**D3. Tất định.** Đề xuất:
- PRNG SplitMix64 tự viết. Mỗi luồng ngẫu nhiên có hạt riêng = SplitMix64 trộn từ `(hồ sơ, chỉ số hạt giống, tên luồng)`, ví dụ luồng `"base.food"`, `"scenario.K1"`.
- Nhờ vậy, thêm K7 ở tầng 2 không làm xê dịch dữ liệu K0–K6 đã báo cáo ở tầng 1, và hai lần chạy ở hai tầng cho cùng số trên phần chung.
- Id giao dịch: `DeterministicGuid.V5(userId, "tx:{n}")`.
- Phân phối dùng phép biến đổi tự viết (Box–Muller cho log-normal, Knuth cho Poisson với λ nhỏ), không dùng `Math.FusedMultiplyAdd` hay song song hóa.
- Số in ra dùng `CultureInfo.InvariantCulture`, xuống dòng `\n`, các dòng sắp theo khóa cố định.

**D4. Lịch dữ liệu và Tết.** Đề xuất:
- Mỗi người có dữ liệu từ **2025-01-01** tới **2026-07-31**. Khởi động: 2025-01 → 2025-06. Mốc đánh giá: mỗi ngày từ **2025-07-01** tới **2026-06-30** (365 mốc). Phần đến 2026-07-31 chỉ để có số thực tế cho tầm 30 ngày của các mốc cuối.
- Tết: mùng 1 Tết Ất Tỵ = **2025-01-29** (rơi vào giai đoạn khởi động, nên mô hình "thấy" một Tết trong lịch sử 90 ngày chỉ ở các mốc đầu); mùng 1 Tết Bính Ngọ = **2026-02-17** (nằm trong giai đoạn đánh giá).
- Nguồn: lịch âm Việt Nam (tính theo UTC+7). Khi viết đặc tả sẽ đối chiếu với thông báo nghỉ Tết chính thức của năm tương ứng và ghi đường dẫn; nếu lệch thì theo thông báo chính thức.
- Bảng ngày cố định nằm trong đặc tả và trong code, không tính âm lịch lúc chạy.

**D5. Người dùng và kịch bản.** Đề xuất:
- 100 **người gốc** = 5 hồ sơ × 20 hạt giống. Mỗi kịch bản Kx là **lớp phủ** lên đúng người gốc đó, nên K1 của người #7 và K0 của người #7 giống hệt nhau trừ phần được cài. Nhờ vậy mọi chỉ số kịch bản báo được **chênh lệch cặp** so với K0, tách được ảnh hưởng của riêng kịch bản.
- Tầng 1: 100 × 7 (K0–K6) = 700 người-năm. Tầng 2: thêm K7–K9 thành 100 × 10.
- K6 (người mới) là lớp phủ "bắt đầu dùng app muộn": cắt bỏ lịch sử trước ngày bắt đầu (rải trong giai đoạn đánh giá), chỉ chấm các mốc từ 14 tới 75 ngày sau ngày bắt đầu.
- Vì K6 đã phủ trường hợp người mới, đề xuất **thay** hồ sơ `new_user` ở phần 1 bằng `small_trader` (tiểu thương: thu nhỏ hằng ngày, không có lương, chi nhập hàng theo tuần). Năm hồ sơ là `stable_worker`, `freelancer`, `student`, `family`, `small_trader`.
- Đặc tả ghi khoảng tham số từng hồ sơ, lấy cùng dải với `agent_eval/personas.py` cho lương và tỉ lệ chi khi hai bên có cùng hồ sơ.

**D6. Mốc và trạng thái tại mốc.** Đề xuất:
- Đồng hồ giả = **T 00:00 giờ VN**, nên `VietnamTime.DateOf(now) = T`.
- Repository giả chỉ trả giao dịch `Date < T`, nên `SpentTodayFlexibleCents = 0`.
- Số dư mỗi ví tại mốc = số dư mở đầu + dòng tiền thô (`Date < T`): gồm vay, hoàn tiền, cặp đảo, chuyển ví, góp/rút quỹ (Q16), theo đúng ví.
- Ví bị loại khỏi dự báo (hồ sơ `family`) vẫn có số dư riêng; tập ví được tính theo cờ của ví.

**D7. Repository giả.** Đề xuất:
- Viết tay trong `Simulation/Fakes/`, nạp từ "thế giới" của bộ sinh và cắt theo mốc. Phương thức service không gọi thì ném `NotSupportedException("backtest: <tên>")`, để một lời gọi ngoài dự kiến hỏng ngay thay vì trả rỗng âm thầm.
- `FakeForecastDataRepository` tái tạo ngữ nghĩa của `ForecastDataRepository` **bằng chính các hàm thuần đó**: `RefundNetting.ForHabits`, lọc `IsLoan`, bỏ `Transfer`, chuyển khoản vượt ranh giới như `GetBoundaryTransfersAsync`.
- Rủi ro là bản giả lệch bản SQL. Chặn bằng **một test hợp đồng tích hợp** (Testcontainers Postgres, như các integration test hiện có): nạp một người tổng hợp (có vay, quỹ, hoàn tiền, chuyển ví) vào DB thật, so ba hàm của `IForecastDataRepository` thật với bản giả ở 5 mốc.

**D8. Chế độ khoản định kỳ.** Đề xuất:
- **Đáp án (tầng 1):** khoản định kỳ là các khoản thật trong đáp án, như một người dùng khai báo đầy đủ, nhưng **chỉ biết thông tin ≤ T**:
  - lịch danh nghĩa (chu kỳ, ngày gốc);
  - số tiền = lần trả gần nhất trước T, chưa trả lần nào thì số khai báo;
  - `NextDueDate` = kỳ danh nghĩa sớm nhất chưa trả. Một kỳ bị bỏ (K3) chỉ "lộ ra" khi đã qua ngày, đúng như với tracker;
  - khoản đã kết thúc trước T thì không còn.
  - Đây là **cận trên**: mô hình được biết khoản nào là định kỳ.
- **Phát lại (tầng 2):** bắt đầu với **không** khoản định kỳ nào (người dùng không khai gì). Mỗi ngày D trong toàn bộ dữ liệu chạy hai bước:
  1. **01:30:** `RecurringDetectionService` thật (đồng hồ = D 01:30), có detect + track trên entity trong bộ nhớ;
  2. **08:00:** phần `RunDailyAsync` của `ForecastAlertService` thật (nhắc hạn, tạm ngừng khoản quá hạn lâu).

  Trạng thái entity sau ngày T−1 là đầu vào của mốc T. Không mô phỏng trigger sau từng giao dịch (`TrackForUserAsync`); chỉ có lượt hằng ngày. Ghi điều này vào hạn chế: production cập nhật sớm hơn vài giờ.
- Cả hai chế độ chạy cùng runner; báo cáo luôn ghi cột `mode`.

**D9. Định nghĩa thực tế.** Đề xuất: dùng `ForecastActuals.NetFlow` + chuyển khoản vượt ranh giới (đúng như `FillActualsAsync`). Số dư thực tế ở tầm h = số dư tại mốc + dòng tiền thô từ T tới T+h (gồm cả hai đầu), khớp với engine (ngày T có chi linh hoạt). Bản chụp ảo dựng bằng `ForecastSnapshotBuilder.Build` để cách tính B0 và tầm trùng với production.

**D10. Chỉ số và thống kê.** Đề xuất: giữ bảng ở phần 1 mục 4, chốt thêm các chi tiết sau.
- Bias = dự kiến − thực tế (âm: dự báo bi quan hơn thực tế).
- "Sai số theo quy mô" = MAE / trung bình |thực tế − số dư tại mốc|; mẫu số < 100.000đ thì không in tỉ lệ.
- WAPE gộp trên mọi mốc của nhóm; MAPE chỉ trên mốc có thực tế ≥ 100.000đ, in kèm số mốc bị loại.
- Khoảng tin cậy 95%: bootstrap 1.000 lần, lấy mẫu lại theo **người gốc** (không theo mốc, vì các mốc của một người tương quan), PRNG có hạt cố định. Áp cho chênh lệch cặp mô hình − baseline và kịch bản − K0.
- Đo `ForecastAccuracyCalculator` hiện có **bằng chính hàm đó** (cột MAE, coverage…). Chỉ số mới (bias, WAPE, theo quy mô, CI) ở `Evaluation/`, không sửa production.

**D11. Baseline.** Đề xuất định nghĩa chính xác:
- B-hold = số dư tại mốc.
- B0 = lấy từ bản chụp (`RecurringOnlyCents`).
- B-mean30 = cùng sự kiện định kỳ (cùng chế độ) + chi linh hoạt mỗi ngày = tổng chi linh hoạt 30 ngày trước mốc / 30. "Chi linh hoạt" lọc như `SpendingBaselineCalculator`: Debit, không loại khỏi dự báo, không khớp khoản định kỳ.
- B-naive = số dư tại mốc + dòng tiền thô của `[T−h, T−1]`.
- B-lastmonth (tầng 3) = tổng chi tháng trước, toàn bộ và theo danh mục.
- Baseline không có tham số chỉnh trên dữ liệu đánh giá; 30 ngày là con số chọn trước, ghi trong đặc tả.

**D12. Dải P25–P75.** Đề xuất: báo tỉ lệ phủ và độ rộng dải theo tầm 7/14/30, theo kịch bản và `confidence`, kèm một câu giải thích vì sao dải cộng theo ngày không phải P25–P75 của tổng. Thêm mục vào REQUIREMENTS §13.4: "Dải dự báo: có đổi cách tính (vd. phân vị của tổng mô phỏng) không — quyết định sau khi có số Phase 96". Không đổi code engine.

**D13. Báo cáo.** Đề xuất:
- **Đầu trang:** dòng tái lập (commit, phiên bản đặc tả, hạt giống gốc, phiên bản và băm SHA-256 của `ForecastSettings` dùng, bước mốc, thời gian chạy). Tiếp theo là **bảng tóm tắt** 8–12 dòng, mỗi dòng một câu có chế độ, ví dụ: "Tầm 14 ngày, chế độ đáp án: số dư lệch trung bình X đ (Y% quy mô dòng tiền), tốt hơn 'số dư giữ nguyên' Z% (CI a–b%)".
- **Thân:** bảng số dư theo tầm × baseline; theo kịch bản (chênh so với K0); theo hồ sơ; low/normal; dải; rồi các mục của tầng 2–3.
- **Cuối:** giả định của bộ sinh (chép từ đặc tả), hạn chế (dữ liệu tổng hợp, cỡ mẫu, không có phản hồi người dùng, phát lại không có trigger theo giao dịch, mục tiêu không đo), cách chạy lại.
- **CSV:** một file mỗi nhóm (`balance.csv`, `flexible.csv`, `band.csv`, sau này `recurring.csv`, `anomaly.csv`, `alerts.csv`, `income.csv`, `budget.csv`, `category.csv`). Cột khóa `mode, scenario, persona, confidence, horizon, method`, rồi các chỉ số. Tiền là số nguyên đồng.
- Báo cáo do chương trình sinh hoàn toàn; không sửa tay. Nhận xét viết tay (nếu có) nằm trong một khối riêng có đánh dấu.

**D14. Thay đổi production (chỉ ctor đồng hồ).** Đề xuất:

| Service | Tầng | Thay đổi |
|---|---|---|
| `ForecastService`, `IncomeForecastService`, `BudgetForecastService`, `RecurringDetectionService`, `ForecastAlertDispatcher` | — | Đã có ctor `internal` nhận đồng hồ; không đụng |
| `AnomalyService` | 2 | Thêm ctor `internal` nhận `Func<DateTimeOffset>`; `VietnamTime.Today()` dòng 99 → `VietnamTime.DateOf(_now())` |
| `ForecastAlertService` | 2 | Như trên cho dòng 99, 151, 223 (tầng 2 cần `RunDailyAsync` cho chế độ phát lại và số dư thấp) |
| `CategoryForecastService` | 3 | Như trên cho dòng 92, 141, 152 |

- Các mốc chỉ để ghi lịch sử (`ResolvedAt`, `FeedbackAt`, `UpdatedAt`, `CreatedAt`) giữ `UtcNow`, vì không ảnh hưởng kết quả dự báo.
- Ctor công khai giữ nguyên chữ ký và gọi `this(…, () => DateTimeOffset.UtcNow)`, nên DI không đổi.
- Test hồi quy: (1) mọi test hiện có của ba service xanh **không sửa**; (2) với mỗi service, một test chạy ctor `internal` với đồng hồ cố định và so kết quả với tính tay từ hàm thuần. `GoalForecastService` không đụng (mục tiêu không đo).

**D15. Cảnh báo số dư thấp (tầng 2).** Đề xuất:
- Ngưỡng của mỗi người do bộ sinh đặt theo đặc tả: **20% thu nhập tháng danh nghĩa, làm tròn 100.000đ**; `small_trader` dùng 20% doanh thu ròng trung bình. Ghi rõ trong báo cáo.
- Chạy qua `ForecastAlertService.CheckUserAsync` thật + `ForecastAlertDispatcher` thật (repository cảnh báo giả, push giả, đồng hồ = mốc), nên chống lặp theo `dedup_key` và `lowBalanceCooldownDays` đúng như production. Không có phản hồi "không chính xác", và giờ yên lặng không đổi đúng/sai.
- **Đợt thiếu tiền thật** = chuỗi ngày liên tiếp có số dư thực tế cuối ngày < ngưỡng.
  - *Bắt được* (recall): có cảnh báo trong `[bắt đầu − lowBalanceHorizonDays, bắt đầu]`.
  - *Đúng* (precision): sau cảnh báo có một đợt thật bắt đầu trong `lowBalanceHorizonDays` ngày.
  - *Thời gian báo trước* = ngày bắt đầu đợt − ngày cảnh báo.
  - *Báo nhầm* = số cảnh báo sai trên một người-tháng.
- Mốc mỗi ngày (không theo bước 3 ngày), vì cảnh báo là sự kiện theo ngày.

**D16. Bất thường (tầng 2).** Đề xuất:
- Chạy `AnomalyService.ScanAsync` thật mỗi ngày (đồng hồ = D 08:00) với `IAnomalyRepository` giả (giữ `transaction_anomalies` trong bộ nhớ, không có phản hồi "bình thường").
- Dương tính = giao dịch bộ sinh **cài** (K1 chi đột xuất, K7 trùng). Cờ trên giao dịch khác báo **hai nhóm riêng**:
  - "cờ trên K0 tự nhiên": đuôi log-normal có thể tạo khoản thật sự bất thường, nên không gộp vào báo nhầm;
  - "cờ trên giao dịch định kỳ / nhập hàng": là báo nhầm rõ ràng.
- Chạy ở cả hai chế độ, vì khoản khớp khoản định kỳ bị bỏ qua khi xét bất thường.

**D17. Kịch bản K5 hai lần.** Đề xuất: chạy K5 với `holidays` rỗng (mặc định production) và một lần với `holidays` = khoảng Tết 2026 (mùng −7 tới mùng 7), hệ số **150%**, chọn trước, ghi trong đặc tả, **khác** hệ số bộ sinh dùng (bộ sinh rút ngẫu nhiên 150–250%). Không chọn hệ số theo kết quả.

**D18. Thời gian chạy.** Đề xuất:
- Đo ở task runner đầu tiên. Ngân sách: lượt đầy đủ ≤ 10 phút trong container SDK 9. Vượt thì chuyển mốc dòng tiền sang mỗi 3 ngày (cảnh báo vẫn mỗi ngày) và ghi vào báo cáo.
- Có `--quick` (2 hạt giống, mốc mỗi 7 ngày) cho test khói.
- Chạy tuần tự, một luồng, để giữ tất định; không dùng `Parallel`.

**D19. CLI (tầng 4).** Đề xuất:
- `--config <file.json>`: một khối `forecast` dạng JSON như trong `SystemConfigDocument`, kiểm bằng validator sẵn có của nhóm `forecast`.
- `--compare <file.json>`: chạy hai lượt trên cùng dữ liệu, báo cáo thêm cột `config` và bảng chênh lệch.
- Không có tham số mặc định nào trong code backtest: không `--config` thì dùng `new ForecastSettings()`, đúng mặc định production.
- Phần `limits` của bản nháp bị bỏ qua, có in cảnh báo.
- Ghi chú trong `docs/ops-api.md` ở mục bản nháp: cách xuất bản nháp ra file và chạy so sánh (thủ công, dev chạy).

**D20. Tài liệu.** Đề xuất:
- REQUIREMENTS: thêm **FR-FCST-19** "Đánh giá dự báo bằng backtest" (Should; tiêu chí = một lệnh, tất định, không rò rỉ, báo cáo theo kịch bản). §13.4 thêm: dải P25–P75 (D12); chỉnh tham số dự báo sau khi có số là quyết định riêng; mục tiêu tiết kiệm chưa đo; phát lại chưa có trigger theo giao dịch. §12.2 ghi lệnh chạy.
- TASKS: Phase 96.
- CLAUDE.md: một đoạn "Phase 96" nêu luật dễ phá: bộ sinh chỉ đổi qua đặc tả có phiên bản; repository giả phải khớp test hợp đồng; production chỉ thêm ctor đồng hồ.
- `.context/ARCHITECTURE.md`: AGENTS §4 cấm sửa khi chưa có duyệt, nên **xin duyệt** thêm một mục ngắn §2.x "Backtest dự báo". Không duyệt thì để ở CLAUDE.md.

**D21. Riêng tư.** Đề xuất: tên nơi chi là tên chung chung (`"Quán cơm"`, `"Điện lực"`, `"Siêu thị"`), không tên người thật; email/khóa không có vì không đụng DB, trừ test hợp đồng D7 (dùng Testcontainers, xóa khi xong). Không log số tiền ra console, chỉ in tiến độ.

### Các bước (TDD, một commit mỗi task)

Kiểm thử theo ba tầng như đã thống nhất: chạy test của lớp đang viết khi làm; unit + test liên quan trước khi commit; toàn bộ suite trước khi merge. Đo mốc test (số pass/fail của `main` @ `32912fc`) ở Task 1 và ghi vào mục "Mốc test".

**Tầng 1 — số dư, chế độ đáp án, baseline (điểm dừng an toàn 1)**

| # | Task | Ghi chú |
|---|---|---|
| 1 | **Đặc tả bộ sinh v1.0**: `docs/evaluation/forecast-backtest-generator-spec.md` (hồ sơ, phân phối, K0–K9, Tết + nguồn, ngưỡng, hạn mức, hệ số K5, B-mean30 = 30 ngày, luật sửa). Ghi mốc test. **Commit riêng, chỉ tài liệu.** | D2, D4, D5, D15, D17 |
| 2 | Khung project: `FinMate.Backtest` vào sln, `InternalsVisibleTo`, dòng `COPY` trong Dockerfile, `Program.cs` rỗng; PRNG SplitMix64 + phân phối + test (cùng hạt → cùng chuỗi; luồng độc lập). Kiểm `dotnet build`, `dotnet test`, `docker build`. | D1, D3 |
| 3 | Bộ sinh lõi: 5 hồ sơ, chi linh hoạt, khoản định kỳ, thu nhập, ví/chuyển ví/vay/quỹ/hoàn tiền (Q16), đáp án; test tất định, test bất biến (số dư = mở đầu + thô; không có `Transfer` vào thói quen). | D3, D5 |
| 4 | Lớp phủ K0–K6 + test (mỗi kịch bản chỉ đổi phần của nó; ngoài phần đó giống K0 từng giao dịch). | D5 |
| 5 | Repository giả + đồng hồ + trạng thái tại mốc (số dư, chế độ đáp án); chạy `ForecastService` thật. Test: **không rò rỉ** (thêm/sửa giao dịch ≥ T không đổi kết quả tại T, so từng số), lời gọi ngoài dự kiến ném lỗi. | D6, D7, D8 |
| 6 | Test hợp đồng tích hợp: repository giả = `ForecastDataRepository` thật trên Postgres (Testcontainers). | D7 |
| 7 | Baseline (B-hold, B0, B-mean30, B-naive) + chỉ số (dùng lại `ForecastSnapshotBuilder`/`ForecastAccuracyCalculator`; thêm bias, theo quy mô, WAPE chi linh hoạt, dải theo tầm, bootstrap CI). Test với số tính tay. | D9–D12 |
| 8 | Runner + báo cáo MD/CSV + CLI (`--seed`, `--out`, `--quick`); đo thời gian (D18). Test: hai lượt `--quick` cùng hạt → **file giống từng byte**. | D13, D18 |
| 9 | Chạy đầy đủ, commit `docs/evaluation/forecast-backtest.md` + CSV; REQUIREMENTS (FR-FCST-19, §13.4, §12.2), TASKS Phase 96, CLAUDE.md. | D20 |

**Tầng 2 — phát lại, số dư thấp, bất thường, K7–K9 (điểm dừng an toàn 2)**

| # | Task | Ghi chú |
|---|---|---|
| 10 | Ctor đồng hồ cho `AnomalyService`, `ForecastAlertService` + test hồi quy. | D14 |
| 11 | Chế độ phát lại (01:30 detection/tracking, 08:00 `RunDailyAsync`) trên `IRecurringItemRepository` giả trong bộ nhớ; báo cáo số dư ở **cả hai** chế độ. Test: phát lại không thấy giao dịch ≥ T; trạng thái tại T chỉ phụ thuộc dữ liệu < T. | D8 |
| 12 | Lớp phủ K7–K9 + test (không làm xê dịch K0–K6). | D3, D5 |
| 13 | Chỉ số phát hiện định kỳ (precision/recall, chu kỳ, số tiền, thời gian phát hiện, lệch `NextDueDate`), đổi giá, tạm ngừng nhầm. | |
| 14 | Cảnh báo số dư thấp qua `CheckUserAsync` + dispatcher thật. | D15 |
| 15 | Bất thường qua `ScanAsync` thật, hai nhóm cờ. | D16 |
| 16 | Chạy lại, cập nhật báo cáo + tài liệu tầng 2. | |

**Tầng 3 — thu nhập, ngân sách, danh mục, cảnh báo còn lại (điểm dừng an toàn 3)**

| # | Task | Ghi chú |
|---|---|---|
| 17 | Ctor đồng hồ cho `CategoryForecastService` + test hồi quy. | D14 |
| 18 | Thu nhập (`IncomeForecastService`): lệch ngày/số tiền thu định kỳ, phủ P25 thu không đều. | |
| 19 | Rủi ro ngân sách (`BudgetForecastService`; `budget_periods` giả tính lại số đã chi qua `TransactionBudgetDelta`/`BudgetSpendScope`) + B-lastmonth; phân loại `at_risk`. Hạn mức theo đặc tả. | D11 |
| 20 | Danh mục tháng tới (`CategoryForecastService`) + B-lastmonth. | |
| 21 | Cảnh báo rủi ro ngân sách, thu nhập trễ, nhắc hạn/quá hạn hóa đơn (dispatcher thật). | |
| 22 | Chạy lại, cập nhật báo cáo + tài liệu tầng 3. | |

**Tầng 4 — so sánh tham số (điểm dừng an toàn 4)**

| # | Task | Ghi chú |
|---|---|---|
| 23 | `--config` / `--compare` + kiểm bằng validator; bảng chênh lệch; test (cùng file hai lần → chênh 0; file sai → lỗi rõ ràng, exit code ≠ 0). | D19 |
| 24 | Tài liệu: `docs/ops-api.md` (cách chạy so sánh bản nháp), REQUIREMENTS §13.4 Phase 95 bỏ mục "chạy thử dự báo trên hồ sơ mẫu" (đã làm bằng CLI, API vẫn để sau). | D19, D20 |

Tổng 24 task, nhiều hơn ước lượng 15 ở phần 1. Phần tăng thêm gồm: commit đặc tả riêng (yêu cầu mới), test hợp đồng repository giả (D7), tách các ctor đồng hồ thành task riêng có test hồi quy, và mỗi tầng có một task chạy lại + tài liệu để dừng an toàn.

### Rủi ro

| Rủi ro | Chặn bằng |
|---|---|
| Repository giả lệch bản SQL → số đo không phản ánh production | Test hợp đồng D7 (Task 6) |
| Bộ sinh bị chỉnh theo kết quả | Commit đặc tả riêng trước runner; luật tăng phiên bản (D2) |
| Phát lại bỏ sót một bước của production | Gọi service thật, không gọi hàm thuần rời rạc; hạn chế "không có trigger theo giao dịch" ghi rõ |
| Tất định vỡ do thứ tự dictionary / culture / dấu phẩy động | D3; test so file từng byte |
| Thời gian chạy vượt ngân sách | D18 (bước 3 ngày, `--quick`) |
| Số đẹp vì dữ liệu tổng hợp | Báo cáo theo kịch bản, chế độ đáp án gắn nhãn "cận trên", mục hạn chế |

### Mốc test

`main` @ `32912fc`: 4293/4295 (đo ở Phase 95 Task 12, cùng commit), hai đỏ có sẵn như trên.

### Cần duyệt

D1–D21, đặc biệt:
- **D5:** thay `new_user` bằng `small_trader` (người mới đã phủ bằng K6);
- **D8:** định nghĩa chế độ đáp án và chế độ phát lại;
- **D15:** ngưỡng 20% thu nhập;
- **D17:** hệ số Tết 150% khi có cấu hình;
- **D20:** thêm mục vào `.context/ARCHITECTURE.md` hay chỉ ghi ở CLAUDE.md.


---

## Kết quả tầng 1 (2026-11-27)

### Đã làm

| Task | Commit | Ghi chú |
|---|---|---|
| 1 | `97a75be` | Đặc tả bộ sinh v1.0, commit riêng trước mọi code. |
| 1b | `89d86e2` | **Đặc tả v1.1** (§0: thiếu sót phát hiện khi viết bộ sinh, **trước** khi có runner hay số đo): λ/σ của `family`, tỉ trọng mặc định cho `freelancer`/`small_trader`, ví trả chi linh hoạt, hệ số nhân vào λ hay số tiền, luồng theo ngày. |
| 2 | `de47879` | Project `FinMate.Backtest` vào solution, dòng `COPY` trong Dockerfile (đã thử `podman build` qua), `InternalsVisibleTo`, SplitMix64 khớp giá trị tham chiếu. |
| 3 | `f8fe12e` | Bộ sinh 5 hồ sơ. Phần rút tham số kịch bản K1–K6 nằm luôn ở commit này vì gắn vào cùng chỗ khai triển; test của chúng ở Task 4. |
| 4 | `6289774` | Test lớp phủ: ngoài phần cài, mỗi kịch bản giống K0 từng giao dịch (trừ hệ quả có chủ đích: chi theo ngày lương, hoàn tiền của khoản mua dịp Tết, nộp tiền mặt của tiểu thương). |
| 5 | `19aeff2` | `WorldView` + repository giả + chế độ đáp án, chạy `ForecastService` thật. Test: không rò rỉ (nhân ba mọi giao dịch từ T, thêm giao dịch đúng lúc T → dự báo tại T giống từng byte JSON), một tick trước T thì thấy, lượt 08:00 chỉ thấy trước 08:00 (S1), số dư lúc chụp + dòng tiền thực tế = số dư dựng lại ở T+h+1. |
| 6 | `b5f9ba1` | Test hợp đồng trên Postgres: ba hàm của `IForecastDataRepository` giống hệt bản giả ở 5 mốc (người gia đình có vay, quỹ, ví bị loại, hoàn đủ và hoàn một phần). |
| 7 | `dad07a9` | Baseline + chỉ số + bootstrap theo người, test bằng số tính tay. |
| 8 | `39aa0b6` | Runner, báo cáo, CLI. Đo: lượt quick 4,3 s (~1 ms/mốc). |
| 9 | `65178f6` + commit tài liệu | Báo cáo thêm câu tóm tắt và cột thiên lệch / tỉ lệ phủ theo hồ sơ (con số gộp năm hồ sơ che mất nguyên nhân). Chạy đầy đủ: 800 thế giới, 785.100 dòng, **63–68 s**, mốc mỗi ngày (không cần bước 3 ngày). Hai lượt liên tiếp cùng image cho checksum giống hệt mọi tệp. |

Toàn bộ test backend: **4467/4469** (mốc 4293/4295, +174, không thêm đỏ; vẫn 2 đỏ sẵn trên `main`).

Production: **không đổi dòng nào** ngoài `InternalsVisibleTo` trong `FinMate.Application.csproj` (tầng 1 không cần ctor đồng hồ mới, D14).

### Số chính (chế độ đáp án, K0, mọi hồ sơ trừ khi ghi khác)

- Số dư tầm 7 / 14 / 30 ngày: MAE 5,2 / 9,1 / 17,8 triệu; thiên lệch −3,9 / −7,1 / −14,5 triệu (dự báo thấp hơn thực tế). So với "giữ nguyên": +3,0% / −22,8% / −126,6%.
- Theo hồ sơ (tầm 14): `stable_worker` tốt hơn giữ nguyên 80,6%, `family` 68,6%, `student` 19,6%, `freelancer` −17,7%, `small_trader` −382%. Nguyên nhân: engine không chiếu thu nhập không đều (chỉ khoản thu định kỳ), nên hai hồ sơ cuối luôn bị dự báo thấp; `small_trader` có thiên lệch đúng bằng −MAE.
- Chi linh hoạt: WAPE 21,7% / 19,3% / 17,9%, thiên lệch −16,8% (trung vị theo ngày dưới tổng của chi tiêu đuôi nặng); B-mean30 WAPE cao hơn nhưng gần như không thiên lệch.
- Dải: gộp 35–36% ở mọi tầm; `stable_worker` 60,8% → 65,6% → 70,5% (rộng dần theo tầm, đúng giả thuyết phần 1), `small_trader` 0%.
- Kịch bản (tầm 14, mốc bị ảnh hưởng, so với K0 cùng mốc): K1 +17,2%, K2 +12,2%, K3 +7,4%, K4 +1,3%, K5 +21,1%, K5h +31,9% (so K5: +8,9%), K6 −3,8%.

### Lệch so với kế hoạch

- Giả thuyết ở phần 1 ("tỉ lệ phủ sẽ vượt xa 50% ở tầm 30") chỉ đúng với hồ sơ thu nhập đều; số gộp lại dưới 50% vì dự báo lệch tâm ở hồ sơ thu không đều. Đã ghi đúng như đo được, không sửa gì.
- Ước lượng thời gian (1–3 phút cho cả bộ, < 1 phút cho dòng tiền): thực đo 63–68 s cho tầng 1.
- Đặc tả tăng lên v1.1 một lần, trước runner (bảng trên).
