# Phase 96 — Đánh giá dự báo bằng backtest

> Trạng thái: **Bước 1 (kiểm kê) xong 2026-11-27, chờ chọn phạm vi.** Chưa thiết kế chi tiết, chưa sửa code.
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
