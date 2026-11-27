# Phase 97 — Sửa mô hình dự báo số dư theo chẩn đoán backtest

> Trạng thái: **Tầng 1 (Task 1–7) xong 2026-11-27, dừng theo S4 chờ quyết định tầng 2.** Chưa push, chưa mở PR. Tập kiểm tra (20261128) chưa chạy lần nào.
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


---

## Tầng 1 — kết quả (Task 2–7, 2026-11-27)

| Task | Commit | Nội dung |
|---|---|---|
| 1 | `19b5c1e` | So F1–F3 trên tập phát triển, chọn F1 theo tiêu chí áp bằng code (mục trên) |
| 2 | `60e770f` | `SpendingBaselineCalculator`: trung bình theo thứ; dải giữ độ rộng cũ quanh nó (S1), `DailyLevels` dùng chung |
| 3 | `e5df624` | `IrregularIncomeBaselineCalculator` (thuần): D2, D3 (đường thận trọng của **thu**), D4 |
| 4 | `d82565f` | `CashflowEngine`: thu vào ba đường, trừ phần nhận hôm nay, hệ số ngày lễ chỉ cho chi; API thêm `irregularIncomeCents`, `incomeBasis` |
| 5 | `d2e638a` | `ForecastService` nối dây; test spendable không đổi (D12); test S3 |
| 6 | `16cb788` | `ForecastMethods.Current = mean_dow_income_v2`; báo cáo độ chính xác lọc theo phương pháp, `?method=` (D9) |
| 7 | `8ff80cd` + commit tài liệu | Báo cáo backtest thêm thu không định kỳ (6b, `income.csv`), tên phương pháp ở header; chạy tập phát triển → `docs/evaluation/phase97/dev-after-t1/`; REQUIREMENTS, `docs/forecast-cashflow-api.md`, TASKS |

**Diễn giải S1 cho phần thu.** S1 nói về dải của **chi** (giữ độ rộng P75/P25 cũ). Thu chưa từng có dải, và với thu dồn vào vài ngày thì P25 = trung vị = P75 = 0, nên áp nguyên S1 sẽ cho đường thận trọng nhận đủ 100% thu dự kiến. Vì vậy với **thu** giữ D3: thận trọng = dự kiến × (P25 / trung bình theo tháng trọn vẹn), lạc quan = dự kiến. Cần chủ sản phẩm xác nhận.

**Test production đổi kỳ vọng** (ghi trong từng commit):
- `DiagnosisTests`: cách tính khớp production là trung bình (c), không còn là trung vị có cắt (a).
- `ForecastAccuracyTests`, `ForecastAccuracyJobTests`: tên phương pháp mới.
- `ForecastFeedbackControllerTests`: đọc bản chụp `median_p25p75` đã gieo bằng `?method=median_p25p75`.

Các test hiện có của `SpendingBaselineCalculator`, spendable, ngân sách, thu nhập, danh mục, purchase-check, cảnh báo **không** đổi kỳ vọng.

### Số tập phát triển (hạt 20261127, bộ sinh v1.2) so với Phase 96

#### Số dư (K0, chế độ đáp án): trước (Phase 96, mô hình cũ, bộ sinh v1.2) → sau (tầng 1). Tiền: triệu đồng

| Hồ sơ | Tầm | MAE | Thiên lệch | Tốt hơn B-hold | Tốt hơn B0 | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |
|---|---|---|---|---|---|---|---|---|
| tất cả | 7 | 5,17 → 3,30 | −3,93 → −0,26 | 3,0% → 38,1% | −30,6% → 16,7% | 5,4% → 39,6% | 36,6% → 59,5% | 36,4% → 41,5% |
| tất cả | 14 | 9,05 → 4,68 | −7,14 → −0,25 | −22,8% → 36,6% | −43,5% → 25,9% | 5,8% → 51,4% | 26,2% → 61,9% | 35,2% → 50,3% |
| tất cả | 30 | 17,83 → 7,28 | −14,49 → −0,27 | −126,6% → 7,5% | −53,1% → 37,5% | 6,3% → 61,8% | −78,5% → 27,1% | 35,0% → 58,1% |
| stable_worker | 7 | 0,88 → 0,83 | 0,15 → −0,31 | 80,8% → 82,0% | 54,3% → 57,2% | −5,2% → 1,4% | 88,2% → 88,9% | 60,8% → 62,1% |
| stable_worker | 14 | 1,19 → 1,00 | 0,51 → −0,34 | 80,6% → 83,7% | 64,2% → 69,8% | −14,9% → 3,3% | 89,9% → 91,5% | 65,6% → 75,1% |
| stable_worker | 30 | 1,92 → 1,37 | 1,33 → −0,43 | 40,5% → 57,7% | 71,1% → 79,4% | −30,5% → 7,2% | 26,2% → 47,5% | 70,5% → 84,6% |
| freelancer | 7 | 7,87 → 9,01 | −7,09 → −0,25 | 1,4% → −12,9% | 0,0% → −14,5% | 3,7% → −10,2% | 33,3% → 23,6% | 35,7% → 17,9% |
| freelancer | 14 | 13,97 → 13,25 | −13,26 → −0,43 | −17,7% → −11,6% | −12,2% → −6,4% | 5,7% → 10,5% | 17,5% → 21,8% | 22,4% → 26,5% |
| freelancer | 30 | 27,72 → 20,93 | −27,42 → −0,91 | −47,7% → −11,5% | −29,2% → 2,5% | 8,0% → 30,5% | −9,5% → 17,3% | 9,5% → 36,5% |
| student | 7 | 1,34 → 1,50 | −0,03 → −0,11 | 24,5% → 15,4% | 9,8% → −1,1% | 33,0% → 24,8% | 55,8% → 50,5% | 41,4% → 38,1% |
| student | 14 | 1,97 → 2,35 | 0,04 → −0,12 | 19,6% → 4,0% | 17,7% → 1,6% | 39,5% → 27,7% | 56,3% → 47,8% | 44,0% → 39,8% |
| student | 30 | 3,27 → 4,33 | 0,21 → −0,11 | −11,8% → −48,2% | 28,4% → 5,2% | 44,2% → 26,1% | 34,4% → 13,1% | 43,8% → 34,5% |
| family | 7 | 2,60 → 2,43 | 0,47 → −0,56 | 69,3% → 71,4% | 41,3% → 45,3% | −5,6% → 1,6% | 80,3% → 81,7% | 44,0% → 47,0% |
| family | 14 | 3,54 → 2,98 | 1,63 → −0,31 | 68,6% → 73,6% | 52,7% → 60,3% | −14,6% → 3,7% | 83,7% → 86,3% | 44,2% → 56,8% |
| family | 30 | 5,56 → 4,02 | 4,12 → 0,12 | 8,6% → 33,8% | 63,2% → 73,3% | −26,5% → 8,4% | 31,1% → 50,1% | 51,3% → 67,8% |
| small_trader | 7 | 13,15 → 2,73 | −13,15 → −0,07 | −244,9% → 28,5% | −222,6% → 33,1% | 5,0% → 80,3% | −153,1% → 47,5% | 0,0% → 42,2% |
| small_trader | 14 | 24,60 → 3,81 | −24,60 → −0,07 | −382,0% → 25,4% | −317,0% → 35,5% | 5,0% → 85,3% | −287,2% → 40,1% | 0,0% → 53,6% |
| small_trader | 30 | 50,69 → 5,74 | −50,69 → −0,00 | −507,2% → 31,2% | −383,3% → 45,3% | 5,0% → 89,2% | −464,6% → 36,1% | 0,0% → 67,0% |

"Tốt hơn X" = 1 − MAE mô hình / MAE baseline (âm là kém hơn). Baseline giữ định nghĩa Phase 96 nên MAE baseline giống hệt trước và sau; chỉ mô hình đổi.

#### Thu không định kỳ (sau tầng 1; trước = không chiếu, thiên lệch −100%)

| Kịch bản | Hồ sơ | Tầm | Cửa sổ | n | WAPE | Thiên lệch |
|---|---|---|---|---|---|---|
| K0 | tất cả | 7 | all | 36500 | 52,7% | −0,8% |
| K0 | tất cả | 14 | all | 36500 | 40,8% | −0,9% |
| K0 | tất cả | 30 | all | 36500 | 30,9% | −1,0% |
| K0 | freelancer | 7 | all | 7300 | 118,8% | −2,2% |
| K0 | freelancer | 14 | all | 7300 | 92,5% | −2,6% |
| K0 | freelancer | 30 | all | 7300 | 70,4% | −3,0% |
| K0 | student | 7 | all | 7300 | 9,3% | −0,1% |
| K0 | student | 14 | all | 7300 | 6,8% | −0,0% |
| K0 | student | 30 | all | 7300 | 5,1% | 0,1% |
| K0 | small_trader | 7 | all | 7300 | 18,1% | −0,1% |
| K0 | small_trader | 14 | all | 7300 | 13,7% | −0,1% |
| K0 | small_trader | 30 | all | 7300 | 10,1% | 0,0% |
| K2 | tất cả | 7 | all | 36500 | 57,7% | 2,2% |
| K2 | tất cả | 7 | affected | 1600 | 59,9% | 5,5% |
| K2 | tất cả | 14 | all | 36500 | 45,6% | 2,1% |
| K2 | tất cả | 14 | affected | 3000 | 45,6% | 2,1% |
| K2 | tất cả | 30 | all | 36500 | 36,0% | 1,8% |
| K2 | tất cả | 30 | affected | 6200 | 34,2% | −0,5% |
| K2 | freelancer | 7 | all | 7300 | 119,8% | −2,7% |
| K2 | freelancer | 7 | affected | 320 | 127,9% | 3,3% |
| K2 | freelancer | 14 | all | 7300 | 93,2% | −3,1% |
| K2 | freelancer | 14 | affected | 600 | 110,5% | 8,5% |
| K2 | freelancer | 30 | all | 7300 | 71,4% | −3,6% |
| K2 | freelancer | 30 | affected | 1240 | 81,3% | 4,2% |
| K2 | student | 7 | all | 7300 | 20,9% | 12,9% |
| K2 | student | 7 | affected | 320 | 8,8% | 0,2% |
| K2 | student | 14 | all | 7300 | 18,8% | 12,9% |
| K2 | student | 14 | affected | 600 | 7,2% | −0,2% |
| K2 | student | 30 | all | 7300 | 17,3% | 13,1% |
| K2 | student | 30 | affected | 1240 | 5,2% | −0,1% |
| K2 | small_trader | 7 | all | 7300 | 19,4% | −1,1% |
| K2 | small_trader | 7 | affected | 320 | 24,9% | 7,0% |
| K2 | small_trader | 14 | all | 7300 | 14,8% | −1,2% |
| K2 | small_trader | 14 | affected | 600 | 16,0% | −0,9% |
| K2 | small_trader | 30 | all | 7300 | 11,5% | −1,2% |
| K2 | small_trader | 30 | affected | 1240 | 12,1% | −2,9% |

#### Kịch bản so với đối chứng cùng mốc (tầm 14, mốc bị ảnh hưởng). MAE: triệu đồng

| Kịch bản | Đối chứng | MAE kịch bản trước → sau | Tăng so với đối chứng trước → sau |
|---|---|---|---|
| K1 | K0 | 11,28 → 9,19 | 17,2% → 70,6% |
| K2 | K0 | 10,68 → 6,85 | 12,9% → 40,9% |
| K3 | K0 | 9,88 → 5,76 | 7,4% → 10,1% |
| K4 | K0 | 9,30 → 4,84 | 1,3% → 2,3% |
| K5 | K0 | 11,23 → 8,22 | 21,1% → 74,0% |
| K5h | K0 | 12,22 → 8,32 | 31,9% → 76,1% |
| K5h | K5 | 12,22 → 8,32 | 8,9% → 1,2% |
| K6 | K0 | 9,28 → 5,87 | −3,8% → 18,4% |

### Nhận xét

- **Gộp:** lệch ở tầm 14 ngày giảm từ 9,05 xuống 4,68 triệu; thiên lệch từ −7,14 về −0,25 triệu. Mô hình giờ tốt hơn mọi baseline ở mọi tầm (trước đó thua "giữ nguyên" ở tầm 14 và 30).
- **Tiểu thương:** từ thua "giữ nguyên" 382% thành thắng 25% (tầm 14); thiên lệch gần 0.
- **Lương cố định, gia đình:** tốt hơn ở mọi tầm, và giờ hơn cả B-mean30 (trước đó kém 15%).
- **Freelancer:** thiên lệch về gần 0, nhưng MAE gần như không giảm ở tầm 7–14. Thu dồn vào vài ngày ngẫu nhiên: trung bình không thiên lệch, nhưng từng mốc lệch nhiều (WAPE thu 92% ở tầm 14). Mô hình theo ngày không đoán được ngày khách trả tiền; đây là bất định, không phải thiên lệch, nên thuộc về dải (tầng 2).
- **Sinh viên kém đi** (tầm 14: 1,97 → 2,35 triệu). Đây là cái giá đã biết của F1: hai khoản học phí 8–15 triệu trong 90 ngày lịch sử làm trung bình chi tăng, tức đúng ngoại lệ đã chấp nhận ở D6. Thiên lệch vẫn gần 0.
- **Dải** (gộp 41,5% / 50,3% / 58,1%): `stable_worker` 62% → 75% → 85% (rộng quá ở tầm dài, đúng hiệu ứng cộng phân vị); freelancer 18–36% (hẹp quá, thiếu bất định của thu); tiểu thương 42–67%. Dải giữ độ rộng cũ nên chưa phản ánh bất định mới của thu; đây là việc của tầng 2.
- **Kịch bản:** ảnh hưởng tương đối so với K0 tăng (K1 +70,6%, K5 +74%), vì mẫu số K0 nhỏ đi một nửa. MAE tuyệt đối của mọi kịch bản đều giảm. Khai ngày lễ Tết (K5h) giờ gần như trung tính so với không khai (+1,2%, trước là +8,9%), vì thiên lệch có sẵn đã hết.
- **K6 người mới:** +18,4% so với K0 cùng mốc (trước −3,8%). Mức phẳng của thu và chi trên ít ngày dao động mạnh hơn trung bình theo thứ.

### Còn mở: khoản thu định kỳ đổi số tiền (S3)

- Ca đã chặn, có test: lương về trễ quá cửa sổ nhận, đúng số tiền. Nó vẫn khớp khoản định kỳ nên không vào thu linh hoạt, và kỳ quá hạn không được chiếu.
- Ca **chưa chặn được:** khoản thu định kỳ đổi số tiền quá dung sai 10% (K2 từ tháng M+2). Khoản định kỳ cập nhật giá mới, nên các lần nhận theo **giá cũ** trong 90 ngày lịch sử không còn khớp và bị tính là thu không định kỳ, trong khi kỳ kế tiếp vẫn được chiếu.
- Số đo:
  - thu không định kỳ của sinh viên ở K2 bị chiếu cao hơn thực tế **12,9%**;
  - thiên lệch số dư của `stable_worker` ở K2 là +0,26 triệu (K0: −0,34), tức lệch thêm khoảng 0,6 triệu ở tầm 14; MAE 1,96 so với 1,68 của mô hình cũ.
- Đề xuất (chưa làm, cần duyệt): khi xét thu linh hoạt, loại khoản thu **cùng người trả và cùng chiều** với khoản thu định kỳ đang theo dõi, **bất kể số tiền**. Lý do: tiền từ người trả lương là lương, dù giá cũ hay mới; `RecurringMatcher.FindPayment` cũng đã coi khoản cùng người trả trong cửa sổ là "lần trả với giá mới".
- Chi linh hoạt có hiện tượng tương tự với hóa đơn đổi giá (K4), nhưng đã có từ trước Phase 97 và nhỏ (+2,3% so với K0); nếu sửa thì sửa cả hai cùng luật.

---

## Quyết định sau tầng 1 (2026-11-27)

1. **Dải phần thu ở tầng 1 = cách hiểu S1 cho phần thu.** S1 (giữ độ rộng cũ, dời quanh đường dự kiến mới) nói về chi. Thu dồn vào vài ngày không có độ rộng cũ để giữ (P25 = trung vị = P75 = 0), nên phần thu theo D3: thận trọng = dự kiến × min(1, P25 / trung bình theo tháng), lạc quan = dự kiến. Đã duyệt.
2. **Sửa tính hai lần khi khoản định kỳ đổi số tiền: luật người trả + thời điểm** (không dùng "cùng người trả bất kể số tiền", vì luật đó loại cả thưởng và dự án lẻ của cùng người, tái tạo H1). Task 7b bên dưới.
3. **Tầng 2: làm** Task 8–10 trên mô hình đã có Task 7b. Dừng sau Task 10, trước tập kiểm tra.

## Task 7b — Luật người trả + thời điểm (2026-11-27)

**Luật** (`RecurringMatcher.PeriodPayments`, `RecurringPaymentFilter`):
- Với mỗi khoản định kỳ có người trả/nhận, mỗi kỳ đến hạn (từ `NextDueDate` lùi về đầu lịch sử) nhận **đúng một** giao dịch cùng người, cùng chiều trong cửa sổ có sẵn `RecurringSchedule.PaymentWindow` (tháng: hạn − 14 tới hạn + ân hạn 7 ngày) làm lần trả của kỳ đó, **bất kể số tiền**.
- Chọn trong cửa sổ: giao dịch trong dung sai trước; không có thì giao dịch gần ngày đến hạn nhất (bằng nhau lấy sớm hơn).
- Giao dịch cùng người ngoài cửa sổ, hoặc cùng cửa sổ nhưng kỳ đó đã có lần trả khác (thưởng, dự án lẻ, mua thêm), vẫn là linh hoạt.
- Luật cũ (khớp trong dung sai, mọi ngày) giữ nguyên; luật mới chỉ loại thêm.
- Cùng một bộ lọc cho thu linh hoạt (`IrregularIncomeBaselineCalculator`, tỉ lệ thận trọng qua tham số tùy chọn của `IncomeEstimator.Irregular`, phần đã nhận hôm nay) và chi linh hoạt (`SpendingBaselineCalculator`, phần đã chi hôm nay). Không thêm tham số cấu hình.
- **Không đổi:** spendable và trang thu nhập (gọi `IncomeEstimator.Irregular` không có bộ lọc), ngân sách, bất thường, baseline của backtest (D15).
- Harness chẩn đoán (`FlexVariants`, `Estimators`) dùng cùng bộ lọc để vẫn dựng lại đúng production.

**Test:** (a) lương +30% → các lần nhận giá cũ không vào thu linh hoạt; (b) thưởng cùng công ty ngoài cửa sổ, và trong cửa sổ khi kỳ đó đã có lương → vẫn là thu linh hoạt; (c) hóa đơn +25% → không đếm hai lần trong chi linh hoạt; (d) phí định kỳ của khách + dự án lẻ (ngoài cửa sổ và trong cửa sổ) → dự án vẫn được tính; cách chọn một lần trả mỗi kỳ; khoản trả trước khi tracker đẩy ngày. Thêm một test qua `ForecastService` (lương tăng ngoài dung sai). Không test cũ nào đổi kỳ vọng.

**Tập phát triển, trước (tầng 1) → sau (Task 7b), tầm 14, chế độ đáp án. Tiền: triệu đồng.**

Số dư, mọi mốc:

| Kịch bản | Hồ sơ | MAE | Thiên lệch |
|---|---|---|---|
| K0 | gộp | 4,68 → 4,67 | −0,25 → −0,18 |
| K0 | stable_worker | 1,00 → 0,98 | −0,34 → −0,22 |
| K0 | family | 2,98 → 2,98 | −0,31 → −0,12 |
| K0 | freelancer / student / small_trader | không đổi (13,25 / 2,35 / 3,81) | −0,43 / −0,12 / −0,07 → 0,01 |
| K2 | gộp | 5,28 → 5,12 | 0,00 → −0,18 |
| K2 | stable_worker | 1,96 → **1,58** (mô hình cũ 1,68) | 0,26 → −0,11 |
| K2 | family | 4,15 → 3,74 | 0,56 → 0,08 |
| K2 | student | 2,42 → 2,43 | 0,07 → −0,08 |
| K4 | gộp | 4,74 → 4,70 | −0,34 → −0,15 |
| K4 | stable_worker | 1,10 → 1,02 | −0,45 → −0,19 |
| K4 | family | 3,12 → 3,04 | −0,50 → −0,06 |

Thu không định kỳ (WAPE / thiên lệch): K2 sinh viên, mọi mốc **18,8% / +12,9% → 8,9% / +2,3%**; K2 gộp 45,6% / +2,1% → 42,6% / −1,0%; K0 không đổi (40,8% / −0,9%).

Chi linh hoạt (WAPE / thiên lệch): K4 `stable_worker` 22,0% / +6,6% → 19,4% / −1,4%; K4 gia đình 22,7% / +5,3% → 20,8% / −1,2%; K0 gộp 16,5% / +0,3% → 16,3% / −0,7%.

**Nhận xét:**
- Bản sửa tác động cả K0: hóa đơn điện/nước dao động quá 10% giữa các tháng trước đây bị đếm hai lần (trong chi linh hoạt và ở ngày đến hạn).
- Ở các mốc "bị ảnh hưởng" của K2/K4 (sát ngày đổi giá), thiên lệch dương tăng nhẹ (`stable_worker` K2 +2,65 → +2,77; gia đình K4 +0,82 → +1,18). Đó là phần không đoán được của việc đổi giá (khoản định kỳ vẫn mang giá cũ tới lần trả giá mới đầu tiên); trước đây phần đếm hai lần che bớt nó.
- Gia đình K0 tầm 30: thiên lệch −0,12 → +0,51 triệu (MAE không đổi 4,02); dải ở tầng 2 sẽ được đo trên mô hình này.
- Lượt chạy chỉ để so, không lưu vào `docs/evaluation/phase97/`; lượt chính thức của tầng 2 là `dev-after/`.

## Task 8 — So B1/B2 trên tập phát triển (2026-11-27, trước khi sửa engine)

- Lệnh: `dotnet FinMate.Backtest.dll --diagnose FILE`, mục C7 (code `Diagnostics/SumBands.cs`, `Diagnosis.SumBands.cs`). Mô hình: tầng 1 + Task 7b. K0, chế độ đáp án, hạt 20261127, 20 hạt giống mỗi hồ sơ, 36.500 mốc mỗi tầm.
- Chuỗi: dòng tiền linh hoạt ròng theo ngày (thu linh hoạt − chi linh hoạt, cùng bộ lọc với production, ngày không có = 0) trong cửa sổ lịch sử 90 ngày; L = h + 1. Hai đầu dải = đường dự kiến + độ lệch; khoản cho vay đến hạn cộng vào đầu trên sau cùng.
- Tiêu chí như §3, áp bằng code; không đổi sau khi thấy số.

#### dải từ tổng h+1 ngày (ô = tỉ lệ số dư thật nằm trong dải / độ rộng trung bình)

| Hồ sơ | Tầm | dải hiện tại (tầng 1) | B1 phân vị tổng cuộn | B2 chuẩn (σ²L + σ²L²/n) |
|---|---|---|---|---|
| stable_worker | 7 | 62,5% / 1.169.496 đ | 39,9% / 648.832 đ | 42,2% / 697.535 đ |
| stable_worker | 14 | 75,3% / 2.193.585 đ | 36,1% / 843.668 đ | 43,5% / 988.682 đ |
| stable_worker | 30 | 84,9% / 4.534.078 đ | 29,3% / 1.078.718 đ | 43,1% / 1.525.824 đ |
| freelancer | 7 | 17,9% / 5.727.104 đ | 46,7% / 11.779.138 đ | 51,8% / 14.546.581 đ |
| freelancer | 14 | 26,5% / 10.740.268 đ | 42,5% / 17.889.836 đ | 44,3% / 20.618.203 đ |
| freelancer | 30 | 36,5% / 22.197.464 đ | 36,9% / 25.322.920 đ | 48,3% / 31.819.856 đ |
| student | 7 | 38,1% / 1.051.356 đ | 40,3% / 600.649 đ | 63,9% / 2.605.995 đ |
| student | 14 | 39,8% / 1.972.228 đ | 37,6% / 878.573 đ | 62,7% / 3.693.725 đ |
| student | 30 | 34,5% / 4.076.966 đ | 30,2% / 4.432.326 đ | 53,7% / 5.700.516 đ |
| family | 7 | 47,3% / 2.459.086 đ | 32,3% / 1.542.616 đ | 34,2% / 1.693.440 đ |
| family | 14 | 55,8% / 4.611.936 đ | 27,7% / 2.152.395 đ | 32,0% / 2.455.623 đ |
| family | 30 | 67,0% / 9.532.573 đ | 22,6% / 3.004.025 đ | 31,8% / 3.908.438 đ |
| small_trader | 7 | 42,3% / 3.749.365 đ | 53,0% / 4.989.281 đ | 48,1% / 4.363.910 đ |
| small_trader | 14 | 53,6% / 7.033.164 đ | 48,1% / 6.248.213 đ | 48,6% / 6.185.275 đ |
| small_trader | 30 | 67,0% / 14.538.700 đ | 42,2% / 8.304.866 đ | 49,6% / 9.545.402 đ |
| all | 7 | 41,6% / 2.831.281 đ | 42,4% / 3.912.103 đ | 48,1% / 4.781.492 đ |
| all | 14 | 50,2% / 5.310.236 đ | 38,4% / 5.602.537 đ | 46,2% / 6.788.302 đ |
| all | 30 | 58,0% / 10.975.956 đ | 32,2% / 8.428.571 đ | 45,3% / 10.500.007 đ |

Mốc không tính được dải (thiếu lịch sử): dải hiện tại (tầng 1) 0, B1 phân vị tổng cuộn 0, B2 chuẩn (σ²L + σ²L²/n) 0.

**Áp tiêu chí (§3, K0):** lệch phủ lớn nhất so với 50% (max \|phủ − 50%\|) trên 15 ô hồ sơ × tầm, nhỏ hơn thắng; điều kiện: độ rộng TB ở tầm 30 của stable_worker + family ≤ dải hiện tại (7.033.326 đ).

| Phương án | Lệch phủ lớn nhất so với 50% | Ô xa nhất | Độ rộng TB tầm 30, stable_worker + family | Kết quả |
|---|---|---|---|---|
| dải hiện tại (tầng 1) | 34,9% | stable_worker 30 | 7.033.326 đ | đối chứng |
| B1 phân vị tổng cuộn | 27,4% | family 30 | 2.041.371 đ | đạt điều kiện |
| B2 chuẩn (σ²L + σ²L²/n) | 18,2% | family 30 | 2.717.131 đ | đạt điều kiện |

**Lựa chọn theo tiêu chí:** B2 chuẩn (σ²L + σ²L²/n).


**Chọn B2.** Nó qua điều kiện độ rộng (2,72 triệu so với 7,03 triệu của dải hiện tại ở tầm 30, `stable_worker + family`) và lệch phủ lớn nhất 18,2% (B1: 27,4%).

**Nhận xét (không đổi lựa chọn):**
- B1 hẹp dần theo tầm (gộp 42% → 38% → 32%), đúng như C4 đã thấy: 90 ngày chỉ có khoảng 60 cửa sổ 31 ngày, chồng lên nhau.
- B2 gần 50% với `stable_worker`, freelancer và tiểu thương ở mọi tầm. Sinh viên rộng quá (54–64%): hai khoản học phí lớn trong lịch sử làm σ lớn. Gia đình hẹp quá (32–34%): phần lệch của gia đình nằm ngoài dòng tiền linh hoạt (khoản định kỳ và khoản vay lệch ngày hoặc lệch tiền), mà B2 chỉ đo bất định của phần linh hoạt.
- Ô xa nhất của B2 là gia đình tầm 30 (31,8%).

## Tầng 2 — kết quả (Task 9–10, 2026-11-27)

- Code: `NetFlowBand` (B2) trong engine khi đủ `minHistoryDays`; dưới ngưỡng giữ các đường của tầng 1. Phương pháp `mean_dow_income_sumband_v3`. Harness gọi thẳng production cho B2; test kiểm dải của engine trùng B2 dựng lại từ lịch sử của harness tới từng đồng.
- Lượt chạy: `docs/evaluation/phase97/dev-after/` (hạt 20261127, bộ sinh v1.2).

### Tập phát triển cuối so với Phase 96 (K0, chế độ đáp án). Tiền: triệu đồng

"vs X" = 1 − MAE mô hình / MAE của baseline X (Phase 96 → cuối); âm là kém hơn. Phủ dải: tỉ lệ số dư thật nằm giữa đường thận trọng và lạc quan (mục tiêu 50%), Phase 96 → tầng 1 → tầng 2. Độ rộng: trung bình của (lạc quan − thận trọng).

| Hồ sơ | Tầm | MAE P96 → T2 | Thiên lệch P96 → T2 | vs B-hold | vs B0 | vs B-mean30 | vs B-naive | Phủ dải P96 → T1 → T2 | Rộng dải TB P96 → T2 |
|---|---|---|---|---|---|---|---|---|---|
| all | 7 | 5,17 → 3,29 | −3,93 → −0,22 | 3,0% → 38,2% | −30,6% → 16,8% | 5,4% → 39,7% | 36,6% → 59,6% | 36,4% → 41,5% → 48,1% | 1,89 → 4,78 |
| all | 14 | 9,05 → 4,67 | −7,14 → −0,18 | −22,8% → 36,6% | −43,5% → 26,0% | 5,8% → 51,4% | 26,2% → 61,9% | 35,2% → 50,3% → 46,2% | 3,55 → 6,79 |
| all | 30 | 17,83 → 7,27 | −14,49 → −0,10 | −126,6% → 7,6% | −53,1% → 37,6% | 6,3% → 61,8% | −78,5% → 27,2% | 35,0% → 58,1% → 45,3% | 7,34 → 10,50 |
| stable_worker | 7 | 0,88 → 0,81 | 0,15 → −0,24 | 80,8% → 82,3% | 54,3% → 57,8% | −5,2% → 2,7% | 88,2% → 89,1% | 60,8% → 62,1% → 42,2% | 1,19 → 0,70 |
| stable_worker | 14 | 1,19 → 0,98 | 0,51 → −0,22 | 80,6% → 84,0% | 64,2% → 70,4% | −14,9% → 5,2% | 89,9% → 91,7% | 65,6% → 75,1% → 43,5% | 2,24 → 0,99 |
| stable_worker | 30 | 1,92 → 1,33 | 1,33 → −0,18 | 40,5% → 58,8% | 71,1% → 80,0% | −30,5% → 9,7% | 26,2% → 48,9% | 70,5% → 84,6% → 43,1% | 4,63 → 1,53 |
| freelancer | 7 | 7,87 → 9,01 | −7,09 → −0,25 | 1,4% → −12,9% | 0,0% → −14,5% | 3,7% → −10,2% | 33,3% → 23,6% | 35,7% → 17,9% → 51,8% | 1,92 → 14,55 |
| freelancer | 14 | 13,97 → 13,25 | −13,26 → −0,43 | −17,7% → −11,6% | −12,2% → −6,4% | 5,7% → 10,5% | 17,5% → 21,8% | 22,4% → 26,5% → 44,3% | 3,59 → 20,62 |
| freelancer | 30 | 27,72 → 20,93 | −27,42 → −0,91 | −47,7% → −11,5% | −29,2% → 2,5% | 8,0% → 30,5% | −9,5% → 17,3% | 9,5% → 36,5% → 48,3% | 7,42 → 31,82 |
| student | 7 | 1,34 → 1,50 | −0,03 → −0,11 | 24,5% → 15,4% | 9,8% → −1,1% | 33,0% → 24,8% | 55,8% → 50,5% | 41,4% → 38,1% → 63,9% | 0,99 → 2,61 |
| student | 14 | 1,97 → 2,35 | 0,04 → −0,12 | 19,6% → 4,0% | 17,7% → 1,6% | 39,5% → 27,7% | 56,3% → 47,8% | 44,0% → 39,8% → 62,7% | 1,85 → 3,69 |
| student | 30 | 3,27 → 4,33 | 0,21 → −0,11 | −11,8% → −48,2% | 28,4% → 5,2% | 44,2% → 26,1% | 34,4% → 13,1% | 43,8% → 34,5% → 53,7% | 3,83 → 5,70 |
| family | 7 | 2,60 → 2,42 | 0,47 → −0,46 | 69,3% → 71,5% | 41,3% → 45,5% | −5,6% → 1,9% | 80,3% → 81,7% | 44,0% → 47,0% → 34,2% | 2,51 → 1,69 |
| family | 14 | 3,54 → 2,98 | 1,63 → −0,12 | 68,6% → 73,7% | 52,7% → 60,3% | −14,6% → 3,8% | 83,7% → 86,3% | 44,2% → 56,8% → 32,0% | 4,71 → 2,46 |
| family | 30 | 5,56 → 4,02 | 4,12 → 0,51 | 8,6% → 33,8% | 63,2% → 73,3% | −26,5% → 8,4% | 31,1% → 50,1% | 51,3% → 67,8% → 31,8% | 9,74 → 3,91 |
| small_trader | 7 | 13,15 → 2,73 | −13,15 → −0,02 | −244,9% → 28,5% | −222,6% → 33,1% | 5,0% → 80,3% | −153,1% → 47,5% | 0,0% → 42,2% → 48,1% | 2,85 → 4,36 |
| small_trader | 14 | 24,60 → 3,81 | −24,60 → 0,01 | −382,0% → 25,4% | −317,0% → 35,5% | 5,0% → 85,3% | −287,2% → 40,1% | 0,0% → 53,6% → 48,6% | 5,36 → 6,19 |
| small_trader | 30 | 50,69 → 5,73 | −50,69 → 0,18 | −507,2% → 31,3% | −383,3% → 45,3% | 5,0% → 89,3% | −464,6% → 36,2% | 0,0% → 67,0% → 49,6% | 11,07 → 9,55 |

MAE và thiên lệch của tầng 2 khác tầng 1 chỉ do Task 7b (dải không đổi đường dự kiến).

**Thu không định kỳ (tầm 14, WAPE / thiên lệch):** K0 gộp 40,8% / −0,9%; K2 gộp mọi mốc 45,6% / +2,1% (tầng 1) → 42,6% / −1,0%; K2 sinh viên 18,8% / +12,9% → 8,9% / +2,3%; K2 freelancer ở mốc bị ảnh hưởng 110,5% / +8,5% (không đổi: thu dự án dời 7–14 ngày ở tháng M, không liên quan khoản định kỳ).

**Phủ dải theo kịch bản (tầm 14, gộp, mọi mốc):** K0 46,2%; K1 49,9%; K2 45,4%; K3 45,1%; K4 45,8%; K5 49,9%; K5h 50,0%; K6 41,6%. Ở mốc bị ảnh hưởng: K1 15,8% (chi lớn đột xuất nằm ngoài dải là đúng nghĩa), K2 34,3%.

### Nhận xét

- **Dải:** gộp 48% / 46% / 45% ở tầm 7/14/30 (tầng 1: 42% / 50% / 58%; Phase 96: 36% / 35% / 35%). Lương cố định 42–44% ở mọi tầm (trước 61% → 85%), tiểu thương 48–50%, freelancer 44–52% (trước 18–37%).
- **Còn lệch:** sinh viên rộng quá (54–64%), vì hai khoản học phí lớn trong 90 ngày làm σ lớn; gia đình hẹp quá (32–34%), vì phần lệch của gia đình nằm ở khoản định kỳ và khoản vay (lệch ngày, lệch tiền), mà dải chỉ đo bất định của dòng tiền linh hoạt. Cả hai không sửa trong Phase 97 (sửa là dò tham số trên tập phát triển).
- **Độ rộng:** hẹp đi với người thu chi đều (lương cố định tầm 30: 4,63 → 1,53 triệu so với Phase 96), rộng ra với người thu không đều (freelancer tầm 14: 3,59 → 20,62 triệu). Dải cũ của freelancer hẹp vì chỉ đo chi; giờ đo cả thu.
- **Sai số:** gộp tốt hơn cả bốn baseline ở mọi tầm. Kém Phase 96 ở hai chỗ: **freelancer tầm 7** (MAE 7,87 → 9,01; giờ thua B-hold, B0, B-mean30): mô hình cũ luôn thấp hơn thực tế (−7,09) nhưng ít dao động, mô hình mới không thiên lệch nhưng chiếu thu dự án đều mỗi ngày trong khi tiền về từng cục; **sinh viên** ở mọi tầm (tầm 30: 3,27 → 4,33; thua B-hold), cái giá đã chấp nhận ở D6.
- **Ảnh hưởng ngoài số liệu:** kết luận "Mua món này có ổn không?" dùng đường thận trọng, nên với người thu chi đều sẽ bớt khắt khe ở tầm dài, với người thu không đều thì khắt khe hơn. Không test nào của purchase-check đổi kỳ vọng.

## Danh sách phương án đã chốt (trước tập kiểm tra)

| # | Chốt | Ở đâu |
|---|---|---|
| 1 | Tập phát triển hạt 20261127; tập kiểm tra hạt 20261128, chạy một lần ở cuối; "trước" từ worktree `e9664f7` | §0, D1, D13 |
| 2 | Bộ sinh v1.2, không đổi trong Phase 97; "thực tế" = `ForecastActuals.NetFlow` | §0 |
| 3 | Chi linh hoạt = F1, trung bình theo thứ trong 90 ngày (ngày không chi = 0) | Task 1, D5–D6 |
| 4 | Thu không định kỳ = F1 trên cùng chuỗi, cộng vào cả ba đường; ít lịch sử thì mức phẳng, 0 trên đường thận trọng | D2, D4 |
| 5 | Lần trả của khoản định kỳ = khớp dung sai, hoặc người trả + cửa sổ `PaymentWindow` quanh kỳ đến hạn (một giao dịch mỗi kỳ, bất kể số tiền); chung cho thu và chi | Task 7b |
| 6 | Dải = B2: dự kiến ± 0,6745 σ_L, σ_L² = σ²L + σ²L²/n trên dòng tiền linh hoạt ròng; cho vay đến hạn chỉ vào lạc quan | Task 8–9, D7 |
| 7 | Dưới `minHistoryDays`: giữ dải tầng 1 (chi theo P75/P25 từng ngày; thu theo D3: thận trọng = dự kiến × min(1, P25/trung bình tháng), lạc quan = dự kiến) | S1, D3, quyết định sau tầng 1 |
| 8 | Hệ số ngày lễ chỉ áp cho chi | Task 4 |
| 9 | Spendable, trang thu nhập, ngân sách, bất thường không đổi | D12, Task 7b |
| 10 | Tên phương pháp: `median_p25p75` → `mean_dow_income_v2` → `mean_dow_income_sumband_v3`; báo cáo độ chính xác theo phương pháp, `?method=` | D9 |
| 11 | Không thêm tham số cấu hình | D10 |
| 12 | API chỉ thêm `irregularIncomeCents`, `incomeBasis`; nghĩa của `lowBalanceCents` / `highBalanceCents` ghi ở `docs/forecast-cashflow-api.md` | D11 |
| 13 | Trả nợ hằng tháng trước hạn: bỏ | D8 |
| 14 | Baseline B-hold / B0 / B-mean30 / B-naive giữ định nghĩa Phase 96 | D15 |

Còn lại theo kế hoạch: Task 11 (`--compare-runs`), Task 12 (tập kiểm tra một lần, báo cáo `docs/evaluation/forecast-model-fixes.md`, CLAUDE.md, toàn bộ suite). Chưa làm, chờ duyệt.

## Task 11–12 — so sánh và tập kiểm tra (2026-11-27/28)

- 11 phương án ở danh sách trên được chủ sản phẩm xác nhận trước khi chạy tập kiểm tra.
- Task 11: `--compare-runs`. Ngưỡng "khác đáng kể" (số của tập phát triển nằm ngoài CI 95% của tập kiểm tra, với tỉ lệ trong dải hoặc mức tốt hơn B-hold) được ghi vào code và commit trước khi chạy tập kiểm tra.
- Task 12:
  - "Trước" chạy từ worktree `e9664f7`; `dev-before` trùng từng byte với CSV của báo cáo Phase 96. `dev-after` trùng CSV với lượt tầng 2, nên thư mục `dev-after-t2/` được đổi tên thành `dev-after/`.
  - Tập kiểm tra chạy một lần mỗi mô hình. Không chỉnh mô hình sau khi thấy số.
  - Kết quả: 1/18 ô khác đáng kể (sinh viên tầm 14, tỉ lệ trong dải 62,7% so với CI 58,2–62,3%). Thu không định kỳ của freelancer ở các mốc bị ảnh hưởng của K2 lệch +35,9% trên tập kiểm tra so với +8,5% trên tập phát triển ở tầm 14 (20 người, tiền dự án về trễ ở tháng M); ở tầm 30 hai tập khớp. Không phải lỗi code.
  - Báo cáo: `docs/evaluation/forecast-model-fixes.md` (bảng do `--compare-runs` sinh, nhận xét ở `docs/evaluation/phase97/notes.md`).
- 5 test `AssumptionReviewTests` đỏ (1 ms, hỏng ở bước khởi tạo fixture) trong lượt chạy toàn bộ ngày 2026-11-27 trên nhánh: không lặp lại. Lượt chạy toàn bộ trên `main` @ `e9664f7` chỉ có 2 test đỏ có sẵn; lượt chạy lại toàn bộ trên nhánh (cùng cách) cũng chỉ có 2 test đỏ có sẵn. Không có log đầy đủ của lượt đỏ (khi đó chỉ lọc dòng Failed), nên không xác định được nguyên nhân. Các test này không đụng tới code của Phase 97.
