# Phase 97 — Sửa mô hình dự báo số dư theo chẩn đoán backtest

> Trạng thái: **Kế hoạch đã duyệt 2026-11-27: D1–D16 theo đề xuất, kèm S1–S4 (S thắng D khi mâu thuẫn).** Bắt đầu Task 1 sau khi Phase 96 merge và nhánh này rebase lên `main`. Dừng sau tầng 1 (S4).
> Nhánh `feat/forecast-model-fixes`, rebase lên `main` @ `e9664f7` (Phase 96 đã merge qua PR #40, gồm bộ sinh v1.2).
> Nguồn: mục "Chẩn đoán" trong `docs/superpowers/plans/2026-11-27-forecast-backtest.md`, báo cáo `docs/evaluation/forecast-backtest.md`. Công cụ đo: `FinMate.Backtest` (kể cả `--diagnose`). Đặc tả bộ sinh **v1.2** (sửa lỗi K2 tiểu thương sau review PR #40; K0 và K5 giống hệt v1.1), dùng cho cả hai tập.

## Đã duyệt (2026-11-27): D1–D16 kèm S1–S4

- **S1 (thay D3):** tầng 1 không để dải dồn một phía. Giữ độ rộng cũ nhưng dời tâm theo đường dự kiến mới: thận trọng = dự kiến + Σ(P75 − trung vị) theo ngày, lạc quan = dự kiến − Σ(trung vị − P25). Có test với chi đuôi nặng (trung bình > P75): dải vẫn bao quanh đường dự kiến. Tầng 2 thay cả cách tính.
- **S2 (thay một phần D6):** thiên lệch chi linh hoạt và thiên lệch thu linh hoạt xét **riêng**, mỗi bên |thiên lệch| ≤ 5% (ngoại lệ sinh viên giữ cho phần chi); WAPE gộp dùng để xếp hạng. Không phương án nào qua thì dừng.
- **S3 (lương trễ):** test ca khoản thu định kỳ về trễ quá dung sai matcher: không được vừa cộng vào thu linh hoạt vừa giữ khoản định kỳ chưa trả trong cùng một dự báo; nếu không chặn được thì ghi rõ và đo. Báo cáo backtest in riêng thiên lệch thu của K2.
- **S4:** dừng sau tầng 1 (Task 7), gửi bảng số tập phát triển so với Phase 96 (hồ sơ × tầm, thiên lệch, so baseline, tỉ lệ phủ dải); chủ sản phẩm quyết có làm tầng 2.
- Phase 96 được push và mở PR; Task 1 bắt đầu sau khi PR đó merge và nhánh này rebase lên `main`. Hai test đỏ có sẵn trên `main` không tính là hồi quy.

## 0. Tập đánh giá (chốt trước khi viết code)

| Tập | Hạt gốc | Dùng để |
|---|---|---|
| Phát triển | `20261127` (Phase 96) | So phương án, chọn, báo số sau mỗi tầng |
| **Kiểm tra** | **`20261128`** | Chạy **một lần** ở cuối phase, sau khi mọi phương án đã chốt |

Chọn `20261128` theo quy tắc "ngày kế tiếp", không vì lý do nào khác. Hạt của mỗi luồng là băm FNV-1a của chuỗi khóa có chứa hạt gốc, nên hai hạt liền nhau vẫn cho dữ liệu độc lập.

Luật đi kèm:
- Không quay lại chỉnh mô hình sau khi thấy kết quả tập kiểm tra. Nếu buộc phải sửa vì lỗi thật, ghi lý do và báo cáo cả hai lần chạy.
- Số "trước" trên tập kiểm tra lấy bằng cách chạy backtest của commit `e9664f7` (`main` khi bắt đầu Phase 97: mô hình cũ + bộ sinh v1.2) trong một worktree riêng, cùng lúc với lần chạy "sau", để không ai nhìn thấy số tập kiểm tra trước khi chốt (D13). Số "trước" trên tập phát triển là báo cáo Phase 96 đã sinh ở `6aa8eb2` (cùng mô hình cũ, cùng v1.2).

## 1. Thu nhập không định kỳ vào đường số dư (vấn đề 1)

**Hiện trạng** (đọc code):
- `ForecastService.ProjectAsync` (`ForecastService.cs:214–221`) chỉ đưa vào engine khoản định kỳ, kịch bản và khoản vay.
- `CashflowEngine.Project` không có số hạng thu nào ngoài sự kiện.
- Thu không đều mức thận trọng (`IncomeEstimator.Irregular`, phân vị 25 của tổng các tháng trọn vẹn) chỉ dùng trong `GetSpendableAsync` → `SpendableCalculator`, và chỉ khi user không có khoản thu định kỳ nào.

**Đề xuất.** Thêm một "mức thu linh hoạt" theo ngày, đối xứng với mức chi linh hoạt:
- Cùng chuỗi ngày 90 ngày, cùng cách ước lượng đã chọn ở §2, nhưng đọc `Credit` thay vì `Debit`.
- Engine cộng nó vào ba đường như trừ chi linh hoạt.

**Vì sao không dùng thẳng `IncomeEstimator.Irregular`:**
- (a) Nó cần ≥ 2 tháng trọn vẹn, nên người có 6 tuần lịch sử không có gì.
- (b) Nó cho một con số mỗi tháng, mất nhịp trong tuần (ngày nghỉ của tiểu thương, CN bán nhiều).
- (c) Nó đo cả tháng hiện tại theo kiểu "đã nhận / còn chờ", hợp với spendable hơn đường số dư.

`IncomeEstimator.Irregular` vẫn dùng ở hai chỗ: tỉ lệ thận trọng cho đường thận trọng ở tầng 1 (D3), và spendable như cũ.

**Vì sao phải là trung bình, không phải trung vị.** Thu của freelancer là vài khoản lớn mỗi tháng rơi vào ngày ngẫu nhiên. Trung vị theo thứ của chuỗi ngày gần như luôn bằng 0, nên dự báo vẫn không có đồng thu nào. Kỳ vọng của một tổng là tổng các kỳ vọng, nên ước lượng phải dựa trên trung bình. Đây là lý giải trước khi đo, không phải dò tham số.

**Chặn tính hai lần và luật loại trừ:**
- Nguồn là `GetTransactionsAsync` (dữ liệu thói quen). Nguồn này đã bỏ vay/mượn (`IsLoan`), chuyển khoản (kể cả góp/rút quỹ nhóm, là `Transfer`), khoản hoàn và cặp đảo (`RefundNetting.ForHabits`), nên Q16 giữ nguyên.
- Chuyển khoản vượt ranh giới **không** vào (giống thói quen chi, Phase 83 S4).
- Loại `Credit` khớp khoản thu định kỳ đang theo dõi hoặc vừa hoàn thành, qua `RecurringMatcher.MatchesAny` với danh sách `excluded` sẵn có (D5 Phase 92). Đây đúng là cách chi linh hoạt loại khoản định kỳ.
- Loại giao dịch người dùng đã đánh dấu loại khỏi dự báo.
- Thu đã nhận **hôm nay** trừ khỏi mức của hôm nay, giống `SpentTodayFlexibleCents`.

**Người ít lịch sử.** Dùng cùng ngưỡng `forecast.minHistoryDays` với chi linh hoạt:
- Đủ ngưỡng: mức theo thứ trong tuần.
- Dưới ngưỡng nhưng có dữ liệu: mức phẳng = tổng thu / số ngày đã có (như nhánh `average` của chi). Đường **thận trọng** khi đó không cộng thu không đều (= 0), vì vài khoản thu ngẫu nhiên trong 1–4 tuần không đủ tin.
- Không có dữ liệu: 0.

Không có nhánh "ngân sách" cho thu.

**Spendable không cộng hai lần.** `GetSpendableAsync` không gọi `CashflowEngine.Project`; nó tính từ `RecurringEvents` cộng `IrregularIncomeCents` riêng. Thay đổi chỉ nằm ở `ProjectAsync` và `CashflowInput`, nên spendable **không đổi**. Một test hồi quy sẽ chứng minh: spendable của một freelancer trước và sau phải bằng nhau. Hai con số vẫn khác ngữ nghĩa (spendable = mức thận trọng còn chờ trong tháng; đường số dư = mức dự kiến theo ngày), ghi vào FR-FCST-05.

**Ai bị ảnh hưởng.** Mọi chỗ đọc `GetCashflowAsync` / `RunScenarioAsync`:
- `/forecast/cashflow`, `/scenario`;
- cảnh báo số dư thấp (sẽ bớt báo nhầm với người thu không đều);
- `PurchaseCheckService` (kết luận có thể đổi từ `shortfall_risk` sang `consider` / `fits_plan` với người thu không đều);
- bản chụp độ chính xác.

`BudgetForecastService` và `IncomeForecastService` không dùng `Project`, nên không đổi.

## 2. Chi linh hoạt (vấn đề 2)

Chẩn đoán C2 trên tập phát triển, tầm 14, gộp:

| Cách | Thiên lệch |
|---|---|
| Trung vị có cắt (production) | −16,7% |
| Trung vị không cắt | −12,9% |
| Trung bình không cắt | +0,3% |

Theo hồ sơ, production lệch −24% đến −27% (đi làm, gia đình) và −42,5% (sinh viên). Trung bình cho WAPE thấp nhất ở 4/5 hồ sơ; sinh viên kém hơn (59% so với 45%) vì hai khoản học phí 8–15 triệu nằm trong 90 ngày lịch sử.

**Ba phương án, kèm lý do trước khi đo:**

| Mã | Cách | Lý do |
|---|---|---|
| F1 | Trung bình theo thứ trong tuần, không cắt | Đại lượng cần dự báo là **tổng** chi; kỳ vọng của tổng là tổng các trung bình. Không thiên lệch nếu thói quen ổn định. Rủi ro: vài ngày rất lớn (học phí, sửa xe) kéo cả tuần lên. |
| F2 | Như F1 nhưng **winsorize** chuỗi ngày ở phân vị 95 (ngày vượt P95 bị chặn bằng P95, không bị bỏ) | Giữ phần lớn đuôi phải (khác cách cắt hiện tại, vốn bỏ hẳn) nhưng chặn ảnh hưởng của vài ngày rất hiếm. P95 chọn trước vì là ngưỡng "hiếm" thông dụng, không dò. Đổi lại thiên lệch âm nhỏ đúng bằng phần đuôi trên P95. |
| F3 | Trung bình **chung mọi ngày** (không theo thứ), không cắt | 90 ngày cho khoảng 13 mẫu mỗi thứ; trung bình theo thứ có phương sai lớn. Gộp lại giảm phương sai nhưng mất nhịp tuần (nhập hàng thứ Hai của tiểu thương, cuối tuần chi nhiều). |

Đã cân nhắc rồi bỏ: "trung bình nhưng loại khoản đã bị đánh dấu bất thường".
- Tầng 1 backtest không mô phỏng `transaction_anomalies`, nên không đo được công bằng.
- Học phí sinh viên không bị bộ phát hiện đánh dấu (danh mục ít mẫu, nơi chi mới), nên cách này không giải quyết ca sinh viên.
- `ForecastService` sẽ phải phụ thuộc thêm `IAnomalyRepository`.

**Cách so.** Mở rộng `--diagnose` (chỉ harness, D5) để tính F1–F3 trên cùng chuỗi ngày, giống C2. Báo WAPE và thiên lệch theo hồ sơ × tầm. Cùng lúc đo thu linh hoạt theo đúng ba cách đó (§1), vì §1 dùng chung ước lượng.

**Tiêu chí chọn** (chốt trước khi đo, xét ở tầm 14, tập phát triển, gộp chi + thu linh hoạt):
1. |thiên lệch gộp| ≤ 5%.
2. WAPE gộp thấp nhất trong các phương án qua (1).
3. Không hồ sơ nào có WAPE chi linh hoạt tệ hơn production quá 5 điểm %. **Ngoại lệ ghi trước:** sinh viên được phép tệ hơn tới 15 điểm % nếu thiên lệch giảm từ −42,5% về trong ±10%, vì với họ thiên lệch là lỗi lớn hơn.
4. Hòa thì chọn phương án đơn giản hơn (F1 < F3 < F2).

Không phương án nào qua thì dừng và báo, không đổi tiêu chí.

**Đường thận trọng / lạc quan ở tầng 1.** Giữ cách cũ (phân vị 75/25 theo ngày), chỉ đổi đường dự kiến. Engine đang ép `high ≥ expected` và `low ≤ expected` theo từng thứ, nên với chi đuôi nặng (trung bình > P75) dải sẽ dồn về một phía. Đây là trạng thái tạm, chấp nhận được vì tầng 2 thay cả dải; ghi vào hạn chế của báo cáo tầng 1.

## 3. Dải từ phân phối của tổng h ngày (vấn đề 3, tầng 2)

Chẩn đoán C4 (đi làm + gia đình):
- Dải chi của engine chứa chi thật 65% → 68% → 70% ở tầm 7/14/30, rộng gấp 1,7× / 2,3× / 3,7× dải P25–P75 của tổng.
- Dải lấy thẳng từ phân vị của tổng cuộn trong 90 ngày lại chỉ chứa 46% → 41% → 32%: quá ít cửa sổ dài, và mức chi trôi theo tháng.

**Hai phương án:**

| Mã | Cách | Lý do |
|---|---|---|
| B1 | Phân vị 25/75 của tổng cuộn L ngày (L = 1…h+1) của chuỗi **dòng tiền linh hoạt ròng** (thu linh hoạt − chi linh hoạt) trong lịch sử, trừ đi trung bình × L | Không giả định phân phối. C4 đã cho thấy 90 ngày không đủ cho L lớn; giữ để đối chứng. |
| B2 | Kỳ vọng ± z·σ_L với z = 0,6745 (P25/P75 của phân phối chuẩn), σ_L² = σ²·L + (σ²/n)·L². σ² là phương sai mẫu của phần dư ngày (dòng tiền linh hoạt ròng trừ mức theo thứ), n là số ngày lịch sử. | Ngày gần như độc lập nên phương sai của tổng cộng dồn theo L (căn bậc hai của tầm). Tổng của nhiều ngày đuôi nặng tiến về chuẩn (định lý giới hạn trung tâm). Số hạng thứ hai là sai số của chính mức trung bình ước lượng từ n ngày, lớn dần theo L²; nó giải thích vì sao tầm dài cần dải rộng hơn √L. Không có tham số dò: z cố định, σ và n lấy từ dữ liệu. |

- Bất định của thu không đều nằm sẵn trong σ, vì chuỗi là dòng tiền **ròng** (§1 + §2).
- Khoản cho vay đến hạn vẫn chỉ vào đường lạc quan (BR-DEBT-16), cộng sau khi tính dải.
- Không đề xuất bootstrap theo khối: phức tạp hơn mà vẫn bị giới hạn 90 ngày như B1.

**Tiêu chí chọn** (tập phát triển, K0, chốt trước): tỉ lệ phủ số dư ở tầm 7/14/30, theo từng hồ sơ và gộp. Chọn phương án có max |phủ − 50%| nhỏ nhất trên các ô (hồ sơ × tầm). Thêm điều kiện: độ rộng trung bình không lớn hơn dải cũ ở tầm 30 với `stable_worker + family`.

Dải chỉ có nghĩa khi tâm đúng, nên tầng 2 làm sau tầng 1 và đo trên mô hình đã sửa tâm.

## 4. Trả nợ hằng tháng trước hạn (vấn đề 4): **đề xuất bỏ**

- Sổ vay không có lịch trả: `loans` có gốc, đã trả, `due_date`; `loan_repayments` chỉ ghi các lần đã trả. Không có số tiền hay ngày trả định kỳ.
- `LoanForecast` chiếu gốc còn lại vào ngày hạn là điều duy nhất dữ liệu cho phép.
- Suy ra lịch từ các lần trả trước là đoán. Rải đều phần còn lại tới hạn sẽ sai với khoản trả một lần.
- Người trả góp đều đã có đường riêng: tạo khoản định kỳ có kỳ cuối (Phase 92). Phần còn lại "liên kết trả góp với sổ vay, cảnh báo đếm hai lần" đã nằm ở §13.4 "Trả góp — phần còn lại".
- Sai số chẩn đoán chỉ +0,5 triệu (gia đình, tầm 14).

Ghi vào hạn chế của báo cáo và §13.4; tầng 3 vì vậy không có việc code.

## 5. Ảnh hưởng ngoài engine

- **`forecast_snapshots.method`:**
  - Đổi `ForecastMethods` sang tên mới mỗi khi cách tính đổi: tầng 1 → `mean_dow_income_v2`; tầng 2 → `mean_dow_income_sumband_v3`.
  - **Sửa luôn chỗ trộn số:** `ForecastAccuracyService.GetReportAsync` hiện lấy mọi bản chụp đã có số thực tế (`ListFilledSinceAsync(from)`) mà không lọc phương pháp, chỉ dán nhãn `MedianBand`. Thêm tham số `method` vào truy vấn (chỉ đổi query, không migration; cột `method` đã có), mặc định là phương pháp đang chạy, và trả `method` thật trong DTO.
  - `GET /ops/forecast/accuracy` thêm query `?method=` (không bắt buộc) để xem số cũ.
  - `config_version` giữ nghĩa cũ (phiên bản cấu hình `forecast`).
- **Tham số mới:** đề xuất **không thêm** vào nhóm `forecast`. Cách ước lượng (trung bình, P95 của F2, z của B2) là định nghĩa mô hình, gắn với tên `method`, không phải ngưỡng nghiệp vụ để Staff chỉnh. Thêm vào cấu hình thì phải sửa validator, `/ops/forecast-config/meta` và test phản chiếu Phase 95 (`ForecastConfigMetaTests.EveryPropertyOfBothSectionsIsDescribed`), và tạo đường để bản nháp tham số đổi mô hình mà không qua backtest. Nếu chủ sản phẩm muốn một tham số, làm đủ ba chỗ đó trong cùng task (D10).
- **Hợp đồng API** (chỉ thêm, không đổi tên hay bỏ trường; app Flutter không rẽ nhánh theo `kind`, `basis` có mặc định):
  - `CashflowDayDto.irregularIncomeCents`: thu linh hoạt dự kiến của ngày (đường dự kiến).
  - `CashflowForecastDto.incomeBasis`: `history` / `average` / `none`.
  - `mainCauses` giữ là danh sách khoản **ra**; không thêm nguyên nhân thu.
  - Ý nghĩa `lowBalanceCents` / `highBalanceCents` đổi ở tầng 2 (dải của tổng). Ghi vào tài liệu.
  - Tài liệu: hiện **không có** tệp API riêng cho `/forecast/cashflow`, chỉ có REQUIREMENTS FR-FCST-01. Đề xuất tạo `docs/forecast-cashflow-api.md` ngắn (trường, nghĩa ba đường, cách tính tầng 1/2) và cập nhật FR-FCST-01, 03, 05, 10, 17.
- **Báo nhóm Flutter:** có thể hiện dòng "thu dự kiến" từ `irregularIncomeCents`, nhãn cho `incomeBasis`; con số dự báo của người thu không đều sẽ cao hơn trước (đúng hơn). Không bắt buộc sửa app.
- **Báo nhóm web admin:** dashboard độ chính xác hiện `method`; có bộ chọn phương pháp nếu muốn xem số cũ.
- **Test production sẽ đổi kỳ vọng** (kiểm lại khi làm, ghi lý do trong commit):
  - `SpendingBaselineCalculatorTests`: kỳ vọng trung vị → trung bình.
  - `CashflowEngineTests`: thêm thu linh hoạt; tầng 2 đổi dải.
  - `ForecastServiceTests`, `ForecastControllerTests`, `RefundForecastTests`, `ForecastBoundaryTransferTests`, `ForecastLoanTests`: chỗ nào có `Credit` không khớp khoản định kỳ trong lịch sử, hoặc assert số dư dự kiến / thận trọng / lạc quan.
  - `ForecastAccuracyTests`, `ForecastAccuracyJobTests`: tên phương pháp, lọc theo phương pháp.
  - Test cảnh báo số dư thấp và purchase-check có lịch sử thu.

  Test của spendable, ngân sách, thu nhập, danh mục **không** được đổi kỳ vọng; đổi tức là có hồi quy.

## 6. Chia tầng

| Tầng | Gồm | Điểm dừng |
|---|---|---|
| 1 | §1 + §2: thu linh hoạt + ước lượng chi mới, tên phương pháp v2, lọc độ chính xác theo phương pháp | Backtest tập phát triển, so với Phase 96 |
| 2 | §3: dải từ tổng h ngày, tên phương pháp v3 | Backtest tập phát triển |
| 3 | §4: không code (đề xuất bỏ); chỉ tài liệu | — |
| Cuối | Tập kiểm tra một lần (trước: worktree `e9664f7`; sau: mô hình cuối), báo cáo so sánh, toàn bộ suite | Dừng chờ duyệt push/PR |

## 7. Quyết định

- **D1.** Tập kiểm tra là hạt gốc `20261128`, ghi ở §0 trước mọi code.
- **D2.** Thu linh hoạt dùng chuỗi ngày, cùng ước lượng với chi linh hoạt, đọc `Credit` không khớp khoản thu định kỳ; nguồn là dữ liệu thói quen (Q16 giữ nguyên). Không dùng thẳng `IncomeEstimator.Irregular` cho đường số dư.
- **D3.** Tầng 1: đường thận trọng cộng thu linh hoạt × min(1, thận trọng / trung bình theo tháng của `IncomeEstimator.Irregular`). Chưa đủ 2 tháng, hoặc nhánh `average`, thì 0. Đường lạc quan cộng mức dự kiến. Tầng 2 thay bằng dải của tổng ròng.
- **D4.** Người ít lịch sử: ngưỡng `minHistoryDays` chung; dưới ngưỡng thì mức phẳng trên đường dự kiến và lạc quan, 0 trên đường thận trọng; không dữ liệu thì 0.
- **D5.** So F1–F3 (và thu linh hoạt theo cùng ba cách) bằng `--diagnose` mở rộng, chỉ trong harness, trước khi đổi production. Ghi bảng và lựa chọn vào kế hoạch rồi mới code (một commit riêng cho lựa chọn).
- **D6.** Tiêu chí chọn chi linh hoạt như §2, chốt ở đây; không phương án nào qua thì dừng.
- **D7.** Dải tầng 2: so B1 với B2 theo tiêu chí §3, cũng bằng harness trước khi đổi engine.
- **D8.** Trả nợ hằng tháng: bỏ, ghi §13.4 và hạn chế.
- **D9.** `ForecastMethods` đổi tên mỗi tầng; `ForecastAccuracyService` lọc theo phương pháp, `?method=` ở API ops.
- **D10.** Không thêm tham số vào nhóm `forecast`. Nếu muốn có, phải kèm validator + meta + test phản chiếu cùng task.
- **D11.** API chỉ thêm `irregularIncomeCents` (ngày), `incomeBasis`. Tạo `docs/forecast-cashflow-api.md`.
- **D12.** Spendable không đổi; có test hồi quy.
- **D13.** Số "trước" trên tập kiểm tra chạy từ worktree `e9664f7` (thay `d82d006`: Phase 96 merge kèm v1.2), cùng lúc với lần chạy "sau", sau khi mọi phương án đã chốt.
- **D14.** Báo cáo cuối `docs/evaluation/forecast-model-fixes.md`:
  - bảng tóm tắt cho người không chuyên: trước/sau × tập phát triển/kiểm tra × tầm 7/14/30, theo hồ sơ, tỉ lệ phủ dải;
  - sinh bằng một chế độ mới `--compare-runs <thư mục trước> <thư mục sau>` đọc các CSV đã có, không sửa tay.

  Các lượt chạy lưu ở `docs/evaluation/phase97/{dev,test}-{before,after}/`; `docs/evaluation/forecast-backtest.md` của Phase 96 giữ nguyên.
- **D15.** Harness cập nhật theo engine (thành phần "thu không định kỳ" dự báo đọc `irregularIncomeCents`). Baseline giữ đúng định nghĩa Phase 96 để số so được.
- **D16.** Ngoài phạm vi, ghi vào hạn chế: lương dời về thứ Sáu khi ngày lương rơi cuối tuần (giả định của bộ sinh); bất thường / phản hồi người dùng trong ước lượng chi.

## 8. Các bước (TDD, một commit mỗi task)

**Tầng 1**

| # | Task |
|---|---|
| 1 | `--diagnose` thêm F1–F3 cho chi và thu linh hoạt (harness). Chạy tập phát triển, ghi bảng + lựa chọn theo D6 vào kế hoạch (commit riêng). |
| 2 | `SpendingBaselineCalculator` theo phương án đã chọn (+ ước lượng dùng chung cho thu). Test mới; test cũ đổi kỳ vọng kèm lý do. |
| 3 | `IrregularIncomeBaseline` (hàm thuần): chuỗi `Credit`, loại khoản khớp định kỳ / đã loại / hoàn / vay / chuyển, nhánh ít lịch sử, tỉ lệ thận trọng (D3, D4). |
| 4 | `CashflowEngine`: thu linh hoạt vào ba đường, trừ phần đã nhận hôm nay, `irregularIncomeCents`, `incomeBasis`. |
| 5 | `ForecastService` nối dây; test hồi quy spendable (D12); `PurchaseCheckService`, cảnh báo số dư thấp chạy trên dữ liệu có thu. |
| 6 | `ForecastMethods` v2 + `ForecastAccuracyService` lọc theo phương pháp + `?method=` (D9). |
| 7 | Harness theo engine (D15); chạy backtest tập phát triển (`docs/evaluation/phase97/dev-after-t1/`); số vào kế hoạch; REQUIREMENTS, `docs/forecast-cashflow-api.md`, TASKS. **Điểm dừng.** |

**Tầng 2**

| # | Task |
|---|---|
| 8 | Harness so B1/B2 trên mô hình tầng 1; ghi lựa chọn (commit riêng). |
| 9 | `CashflowEngine` dải theo phương án chọn; `ForecastMethods` v3; test. |
| 10 | Backtest tập phát triển, tài liệu. **Điểm dừng.** |

**Cuối**

| # | Task |
|---|---|
| 11 | `--compare-runs` + test. |
| 12 | Tập kiểm tra một lần (trước từ worktree `e9664f7`, sau từ mô hình cuối); `docs/evaluation/forecast-model-fixes.md`; toàn bộ suite; REQUIREMENTS §13.4 (đóng mục "Dự báo sau backtest Phase 96" phần đã làm, ghi D8, D16), TASKS, CLAUDE.md. Dừng chờ duyệt push/PR. |

## Cần duyệt

D1–D16, đặc biệt:
- **D3:** dải ở tầng 1 trước khi có tầng 2.
- **D6:** tiêu chí chọn, nhất là ngoại lệ cho sinh viên.
- **D8:** bỏ trả nợ hằng tháng.
- **D10:** không thêm tham số cấu hình.
- Nhánh tạm tách từ `feat/forecast-backtest` vì Phase 96 chưa merge.


---

## Task 1 — So F1–F3 trên tập phát triển (2026-11-27, trước khi sửa production)

- Lệnh: `dotnet FinMate.Backtest.dll --diagnose FILE` (mục C6, code `Diagnostics/Estimators.cs`, `Diagnosis.Estimators.cs`).
- Dữ liệu: hạt gốc 20261127, bộ sinh v1.2, 100 người gốc, K0, chế độ đáp án, mốc mỗi ngày.
- Mọi phương án tính trên **cùng chuỗi ngày** mà `SpendingBaselineCalculator` dùng: 90 ngày, ngày không có = 0, loại khoản khớp định kỳ và khoản bị loại khỏi dự báo, ≥ 3 mẫu mỗi thứ không thì dùng mọi ngày.
- Thu linh hoạt dùng `Credit` của cùng chuỗi. "Thực tế" của thu là thành phần "thu không định kỳ" của chẩn đoán C1.
- Tiêu chí (D6 sửa bởi S2) được **áp bằng code**, không chọn bằng mắt.

#### F1–F3 cho chi và thu linh hoạt (ô = WAPE / thiên lệch so với thực tế)

Chi linh hoạt:

| Hồ sơ | Tầm | production | F1 trung bình theo thứ | F2 trung bình theo thứ, winsorize P95 | F3 trung bình chung |
|---|---|---|---|---|---|
| stable_worker | 7 | 31,3% / -24,1% | 26,1% / 2,1% | 25,6% / -2,8% | 26,1% / 2,1% |
| stable_worker | 14 | 27,6% / -23,9% | 19,9% / 2,3% | 19,4% / -2,7% | 19,9% / 2,3% |
| stable_worker | 30 | 25,4% / -23,9% | 15,4% / 2,4% | 15,0% / -2,6% | 15,4% / 2,4% |
| freelancer | 7 | 31,4% / -24,4% | 26,1% / -1,3% | 25,8% / -6,4% | 26,0% / -1,3% |
| freelancer | 14 | 27,6% / -24,3% | 20,0% / -1,1% | 19,9% / -6,2% | 19,9% / -1,0% |
| freelancer | 30 | 25,5% / -24,1% | 15,4% / -0,9% | 15,5% / -6,0% | 15,4% / -0,9% |
| student | 7 | 47,5% / -42,6% | 61,9% / -0,1% | 43,5% / -28,9% | 61,8% / -0,1% |
| student | 14 | 44,9% / -42,5% | 59,0% / -0,0% | 39,1% / -28,8% | 59,0% / -0,0% |
| student | 30 | 43,5% / -42,5% | 57,2% / -0,1% | 36,0% / -28,9% | 57,1% / -0,1% |
| family | 7 | 33,1% / -26,9% | 27,4% / 1,6% | 26,6% / -4,2% | 27,3% / 1,6% |
| family | 14 | 30,0% / -26,9% | 21,2% / 1,6% | 20,7% / -4,2% | 21,2% / 1,6% |
| family | 30 | 28,0% / -26,8% | 16,5% / 1,7% | 16,1% / -4,1% | 16,5% / 1,7% |
| small_trader | 7 | 9,8% / -6,1% | 8,4% / -0,0% | 8,6% / -1,9% | 18,8% / 0,0% |
| small_trader | 14 | 8,3% / -6,1% | 6,4% / -0,0% | 6,6% / -1,9% | 11,7% / 0,0% |
| small_trader | 30 | 7,4% / -6,2% | 4,7% / -0,1% | 5,1% / -2,0% | 9,4% / -0,1% |
| all | 7 | 21,7% / -16,8% | 20,1% / 0,2% | 18,4% / -5,3% | 25,6% / 0,3% |
| all | 14 | 19,3% / -16,7% | 16,5% / 0,3% | 14,7% / -5,3% | 19,2% / 0,3% |
| all | 30 | 17,9% / -16,7% | 13,6% / 0,3% | 11,8% / -5,3% | 16,1% / 0,3% |

Thu linh hoạt (thu không định kỳ; production = 0 vì không chiếu):

| Hồ sơ | Tầm | production | F1 trung bình theo thứ | F2 trung bình theo thứ, winsorize P95 | F3 trung bình chung |
|---|---|---|---|---|---|
| stable_worker | 7 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| stable_worker | 14 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| stable_worker | 30 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| freelancer | 7 | 100,0% / -100,0% | 118,8% / -2,2% | 109,7% / -48,6% | 118,6% / -2,2% |
| freelancer | 14 | 100,0% / -100,0% | 92,5% / -2,6% | 93,5% / -48,7% | 92,4% / -2,5% |
| freelancer | 30 | 100,0% / -100,0% | 70,4% / -3,0% | 79,3% / -48,9% | 70,3% / -3,0% |
| student | 7 | 100,0% / -100,0% | 9,3% / -0,1% | 9,7% / -2,9% | 24,0% / 0,0% |
| student | 14 | 100,0% / -100,0% | 6,8% / -0,0% | 7,4% / -2,8% | 14,6% / 0,1% |
| student | 30 | 100,0% / -100,0% | 5,1% / 0,1% | 5,8% / -2,7% | 12,6% / 0,2% |
| family | 7 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| family | 14 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| family | 30 | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) | — (thực tế 0) |
| small_trader | 7 | 100,0% / -100,0% | 18,1% / -0,1% | 18,1% / -2,5% | 18,7% / -0,1% |
| small_trader | 14 | 100,0% / -100,0% | 13,7% / -0,1% | 13,8% / -2,5% | 13,9% / -0,1% |
| small_trader | 30 | 100,0% / -100,0% | 10,1% / 0,0% | 10,2% / -2,4% | 10,2% / 0,0% |
| all | 7 | 100,0% / -100,0% | 52,7% / -0,8% | 49,6% / -18,4% | 53,4% / -0,8% |
| all | 14 | 100,0% / -100,0% | 40,8% / -0,9% | 41,2% / -18,5% | 41,2% / -0,9% |
| all | 30 | 100,0% / -100,0% | 30,9% / -1,0% | 34,1% / -18,6% | 31,2% / -1,0% |

**Áp tiêu chí (tầm 14, gộp):** |thiên lệch chi| ≤ 5% và |thiên lệch thu| ≤ 5% (xét riêng); WAPE chi của mỗi hồ sơ không tệ hơn production quá 5 điểm % (sinh viên: tới 15 điểm nếu |thiên lệch chi của sinh viên| ≤ 10%); qua hết thì xếp theo WAPE gộp chi + thu; hòa thì F1 < F3 < F2.

| Phương án | Thiên lệch chi | Thiên lệch thu | Hồ sơ vi phạm | WAPE gộp chi + thu | Kết quả |
|---|---|---|---|---|---|
| F1 trung bình theo thứ | 0,3% | -0,9% | — | 28,9% | đạt |
| F2 trung bình theo thứ, winsorize P95 | -5,3% | -18,5% | — | 28,2% | không đạt |
| F3 trung bình chung | 0,3% | -0,9% | — | 30,4% | đạt |

**Lựa chọn theo tiêu chí:** F1 trung bình theo thứ.

**Kết luận Task 1: chọn F1 (trung bình theo thứ trong tuần, không cắt) cho cả chi và thu linh hoạt.**
- F1 và F3 đều đạt cả hai trần thiên lệch. F1 có WAPE gộp thấp hơn (28,9% so với 30,4%), vì F3 mất nhịp tuần: WAPE chi của tiểu thương tăng từ 6,4% lên 11,7%, thu của sinh viên từ 6,8% lên 14,6%.
- F2 không đạt. Winsorize ở P95 cắt mất phần lớn thu của freelancer (thu dồn vào vài ngày mỗi tháng): thiên lệch thu −18,5% gộp, −48,7% với freelancer. Chi cũng lệch −5,3%, vượt trần.
- Sinh viên dùng đúng ngoại lệ đã chốt trước: WAPE chi 59,0% so với 44,9% của production (+14,1 điểm, dưới trần 15), thiên lệch chi về 0,0% (production −42,5%). Nguyên nhân WAPE tăng đã biết từ chẩn đoán: hai khoản học phí lớn nằm trong 90 ngày lịch sử làm trung bình dao động.
- Với freelancer, WAPE thu còn cao (92,5% ở tầm 14) dù thiên lệch nhỏ (−2,6%): thu dồn vào vài ngày ngẫu nhiên nên từng mốc lệch nhiều, nhưng trung bình không thiên lệch. Phần bất định này để dải của tầng 2 xử lý.
- **Không đổi tiêu chí sau khi xem số.** Task 2–4 cài F1 vào production.
