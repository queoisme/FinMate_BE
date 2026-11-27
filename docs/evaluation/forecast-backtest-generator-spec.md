# Đặc tả bộ sinh dữ liệu tổng hợp cho backtest dự báo

> **Phiên bản 1.1** (2026-11-27). Hằng `GeneratorSpec.Version` trong `backend/FinMate.Backtest` phải bằng số này, và báo cáo in số này.
> Kế hoạch: `docs/superpowers/plans/2026-11-27-forecast-backtest.md` (Phase 96, D2–D5, D15, D17, S2).
> Tài liệu này được commit **trước** mọi dòng code của bộ sinh và runner. Code làm theo tài liệu, không làm ngược lại.

## 0. Luật sửa đặc tả

- Chỉ sửa khi có **lỗi thật**: bộ sinh làm khác đặc tả, hoặc đặc tả tạo ra dữ liệu mà production không thể có (ví dụ một loại giao dịch không tồn tại).
- Mỗi lần sửa phải: tăng phiên bản (1.1, 1.2…), ghi lý do vào mục 10 "Lịch sử phiên bản", và cập nhật hằng `GeneratorSpec.Version`.
- **Không sửa vì số đo xấu.** Không chỉnh phân phối, tham số hay kịch bản sau khi đã thấy kết quả để mô hình trông tốt hơn.
- Các con số "chọn trước" (ngưỡng, hệ số, cửa sổ baseline) ghi ở đây và không được chọn lại theo kết quả.

## 1. Mục đích và nguyên tắc

Bộ sinh tạo các "thế giới" giao dịch giả cho backtest dự báo của FinMate. Mỗi thế giới có **đáp án**: khoản định kỳ thật, giao dịch bất thường được cài, ngày xảy ra từng kịch bản. Đáp án chỉ dùng để chấm điểm, không bao giờ được đưa vào mô hình.

Nguyên tắc **chống tự chấm bài**: bộ sinh **không** được giả định giống mô hình. Mô hình dự báo chi linh hoạt bằng trung vị theo thứ trong tuần của 90 ngày gần nhất. Bộ sinh thì dùng những thứ mô hình không biết:

- đuôi nặng (log-normal);
- hiệu ứng ngày lương;
- mức chi trôi dần theo tháng;
- ngày trả định kỳ lệch;
- hóa đơn dao động theo mùa;
- các kịch bản đột biến ở mục 6.

Không có dữ liệu người dùng thật. Tên nơi chi là tên chung chung, không phải tên người hay doanh nghiệp có thật.

## 2. Tất định

- **PRNG:** SplitMix64. Trạng thái 64 bit, bước `state += 0x9E3779B97F4A7C15`, trộn `z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9; z = (z ^ (z >> 27)) * 0x94D049BB133111EB; z ^ (z >> 31)`.
- **Hạt của một luồng** = SplitMix64 trộn từ chuỗi khóa `"{hạt gốc}|{hồ sơ}|{chỉ số hạt giống}|{tên luồng}"`, qua FNV-1a 64 bit. Mỗi quá trình ngẫu nhiên có luồng riêng, đặt tên cố định (ví dụ `base.flex.food`, `base.salary`, `scenario.K1`). Hệ quả: thêm một luồng mới không làm xê dịch dữ liệu của luồng cũ.
- **Hạt gốc mặc định:** `20261127`.
- **Số thực đều** trong [0, 1) = `(next >> 11) * 2^-53`.
- **Chuẩn tắc:** Box–Muller, mỗi lần gọi dùng hai số đều, chỉ lấy một giá trị (không giữ số dư, để thứ tự gọi không ảnh hưởng nhau).
- **Poisson** (λ ≤ 10): thuật toán Knuth.
- **Log-normal:** `trung vị × exp(σ × Z)`.
- **Làm tròn (S4):** mọi số tiền làm tròn tới **1.000đ** ngay sau phép biến đổi phân phối, kiểu `long`, làm tròn nửa lên. Số tiền nhỏ nhất của một giao dịch là 1.000đ.
- **Id:** `DeterministicGuid.V5` trên `userId` và số thứ tự. `userId = DeterministicGuid.V5(namespace backtest, "{hồ sơ}|{chỉ số hạt giống}")`, **giống nhau ở mọi kịch bản** của cùng một người gốc.
- **Không song song hóa.** Không đọc đồng hồ máy. Không phụ thuộc culture (`InvariantCulture`).

## 3. Lịch

| Mốc | Ngày |
|---|---|
| Ngày dữ liệu đầu tiên | 2025-01-01 |
| Giai đoạn khởi động | 2025-01-01 → 2025-06-30 |
| Mốc dự báo đánh giá | mỗi ngày 2025-07-01 → 2026-06-30 (365 mốc) |
| Ngày dữ liệu cuối cùng | 2026-07-31 (để có số thực tế cho tầm 30 ngày của mốc cuối) |

- Mọi giao dịch có `TransactedAt` theo giờ VN (UTC+7). Giờ trong ngày rút đều từ khung hợp lý của từng loại: chi linh hoạt 07:00–22:00, lương/hóa đơn 08:00–17:00, bán hàng 06:00–21:00.
- **Tết** (bảng cố định, không tính âm lịch lúc chạy):

| Năm | Mùng 1 Tết | Nguồn |
|---|---|---|
| Ất Tỵ | **2025-01-29** | Thông báo nghỉ Tết Ất Tỵ 2025 của Bộ LĐ-TB-XH (được Thủ tướng đồng ý): nghỉ từ 25/1/2025 (26 tháng Chạp) tới 2/2/2025 (mùng 5), nên mùng 1 = 29/1/2025. Ví dụ đưa tin: <https://vneconomy.vn/chinh-thuc-co-lich-nghi-tet-at-ty-bat-dau-tu-26-thang-chap.htm> |
| Bính Ngọ | **2026-02-17** | Thông báo 9441/TB-BNV ngày 16/10/2025 của Bộ Nội vụ: nghỉ từ 14/2/2026 (27 tháng Chạp) tới 22/2/2026 (mùng 6), nên mùng 1 = 17/2/2026. Ví dụ đưa tin: <https://vov.vn/xa-hoi/chinh-thuc-lich-nghi-tet-nguyen-dan-va-quoc-khanh-nam-2026-post1238570.vov> |

Tết 2025 rơi vào giai đoạn khởi động; nó nằm ngoài cửa sổ lịch sử 90 ngày của mọi mốc đánh giá. Hiệu ứng Tết chỉ có trong kịch bản K5 (Tết 2026), không có trong K0.

## 4. Ví, danh mục, loại giao dịch (khớp production)

- **Danh mục** dùng slug hệ thống có thật trong `CategorySeeder`: `food`, `transport`, `shopping`, `entertainment`, `health`, `education`, `housing`, `bills`, `family`, `income`, `loan`, `other`. Id danh mục trong bộ nhớ = `DeterministicGuid.V5(namespace, "category:{slug}")`.
- **Loại giao dịch** chỉ dùng những gì production lưu, theo Q16 (Phase 90):

| Bộ sinh tạo | Lưu như production | Vào thói quen chi / thu? | Vào số dư thực tế? |
|---|---|---|---|
| Chi / thu thường | `Debit` / `Credit` | Có | Có |
| Cho vay, đi vay, trả nợ, được trả nợ | `Debit` / `Credit` danh mục `loan` | Không (`IsLoan`) | Có |
| Hoàn tiền | `Credit`, `relation = refund`, `related_transaction_id` → khoản chi gốc | Không (trừ vào khoản gốc qua `RefundNetting`) | Có |
| Chuyển giữa hai ví được tính | `Transfer`, có ví đối ứng | Không | Không đổi tổng |
| Chuyển từ ví được tính sang ví bị loại | `Transfer`, có ví đối ứng | Không (chỉ vào bộ khớp định kỳ, phía tiền ra) | Có (tiền ra khỏi tập được tính) |
| Góp quỹ nhóm | `Transfer`, không ví đối ứng, có `group_fund_entry_id`, hướng ra | Không | Có |

- Mọi giao dịch là `Confirmed`. Không giao dịch nào `ExcludeFromForecast` (không có người dùng đánh dấu). Không có cặp đảo (`reversal`) hay liên kết trùng: ghi vào hạn chế.
- **Tên nơi chi:** mỗi nơi có một tên chuẩn; khi sinh, có 20% xác suất viết khác kiểu, chỉ đổi hoa/thường, thêm khoảng trắng hoặc bỏ dấu. Ba kiểu đó cho cùng `CounterpartyKey`, nên không có biến thể kiểu "EVN HCMC 0123" (ghi vào hạn chế).

## 5. Hồ sơ (năm hồ sơ × 20 hạt giống = 100 người gốc)

Ký hiệu: `U[a,b]` là phân phối đều (số tiền làm tròn 100.000đ trừ khi ghi khác), `LN(m,σ)` là log-normal với trung vị m.

### 5.1 Chi linh hoạt (chung cho mọi hồ sơ, tham số riêng ở từng hồ sơ)

- Mỗi hồ sơ có **ngân sách chi linh hoạt danh nghĩa mỗi tháng** `F` và tỉ trọng theo danh mục.
- Mỗi ngày, với mỗi danh mục: số lần chi ~ Poisson(λ_c × hệ số thứ × hệ số ngày lương), số tiền mỗi lần ~ LN(m_c, σ_c). `m_c` được chọn sao cho kỳ vọng theo tháng ≈ `F × tỉ trọng`: `m_c = F × tỉ trọng / (30,4 × λ_c × exp(σ_c²/2))`.
- **Hệ số thứ trong tuần:** T2–T5 = 0,9; T6 = 1,1; T7 = 1,3; CN = 1,2.
- **Hiệu ứng ngày lương:** trong 5 ngày kể từ một khoản thu lớn (lương, trợ cấp, khoản thu ≥ 30% thu nhập tháng danh nghĩa), λ nhân thêm `1 + u`, với `u ~ U[0,10; 0,30]` cố định theo người (số thực, không làm tròn).
- **Trôi mức chi:** hệ số tháng thứ k (tính từ 2025-01) = `(1 + r)^k`, với `r ~ U[−0,01; +0,02]` mỗi tháng, cố định theo người.
- σ_c mặc định: `food` 0,5; `transport` 0,6; `shopping` 0,9; `entertainment` 0,8; `health` 1,0; `family` 0,8; `other` 1,0. λ_c mặc định (lần/ngày): `food` 1,6; `transport` 0,5; `shopping` 0,25; `entertainment` 0,2; `health` 0,05; `family` 0,15; `other` 0,15.
- **Tỉ trọng mặc định** (dùng khi hồ sơ không ghi riêng: `freelancer`, `small_trader`): `food` 45%, `transport` 15%, `shopping` 20%, `entertainment` 12%, `health` 4%, `other` 4%.
- **Ví trả chi linh hoạt:** ví ngân hàng (ví đầu tiên của hồ sơ), trừ khi hồ sơ ghi khác (`stable_worker`: 30% số lần chi `food` bằng tiền mặt).
- **Hệ số áp vào đâu:** hệ số thứ, hiệu ứng ngày lương, hệ số Tết (K5) và đổi thói quen (K8) nhân vào **λ** (số lần chi); trôi mức chi nhân vào **trung vị số tiền** `m_c`.
- **Luồng theo ngày:** số lần chi và số tiền của mỗi (ngày, danh mục) lấy từ luồng riêng `base.flex.{danh mục}.{yyyy-MM-dd}`, để một kịch bản đổi λ ở vài ngày không làm đổi dữ liệu các ngày khác.
- **Hoàn tiền:** mỗi khoản chi `shopping` có 3% xác suất được hoàn sau 3–10 ngày (rút đều). Trong số đó 60% hoàn 100%, 40% hoàn một phần 20–70%.

### 5.2 Khoản định kỳ (có đáp án)

- Mỗi khoản có: tên chuẩn, chiều, danh mục, ví, chu kỳ (`Weekly`/`Monthly`), ngày gốc, số tiền danh nghĩa theo từng kỳ, ngày bắt đầu, ngày kết thúc (nếu có), các kỳ bị bỏ, các lần đổi giá.
- **Ngày trả thực tế** = ngày đến hạn + độ lệch rút đều trong `{−2,…,+3}` ngày. Riêng lương: không lệch, nhưng nếu rơi vào T7 thì trả T6 trước đó, rơi vào CN thì trả T6 trước đó (hai ngày).
- **Hóa đơn điện:** số tiền tháng = gốc × hệ số mùa (T5–T8 = 1,4; T4, T9 = 1,15; còn lại 1,0) × `U[0,9; 1,1]`, làm tròn 1.000đ.
- **Nước:** gốc × `U[0,85; 1,15]`.
- Các khoản khác số tiền cố định, trừ khi kịch bản đổi.

### 5.3 Năm hồ sơ

**`stable_worker`** — lương cố định.
- Ví: `Ngân hàng` (được tính), `Tiền mặt` (được tính). Số dư mở đầu: ngân hàng `S × U[0,5; 2,0]`, tiền mặt `U[0,5tr; 2tr]`.
- Lương `S ~ U[9tr; 18tr]` vào ngân hàng, ngày 5 hoặc 10 (rút đều).
- Định kỳ:
  - `Tiền nhà` = `S × U[0,20; 0,30]`, ngày 1–5;
  - `Điện lực` gốc `U[300k; 700k]` (làm tròn 1.000đ), ngày 15–20;
  - `Nước sạch` gốc `U[80k; 200k]`, ngày 15–20;
  - `Internet` `U[180k; 300k]`, ngày 10;
  - `Cước di động` `U[100k; 200k]`, ngày 20.
- Chi linh hoạt `F = S × U[0,35; 0,50]`; tỉ trọng `food` 45%, `transport` 15%, `shopping` 20%, `entertainment` 12%, `health` 4%, `other` 4%. 30% số lần chi `food` trả bằng tiền mặt.
- Rút tiền mặt: ngày 6 và ngày 21, chuyển ngân hàng → tiền mặt `U[1tr; 2tr]` (hai ví cùng được tính, không đổi tổng).

**`freelancer`** — thu không đều.
- Ví: `Ngân hàng`, `Ví điện tử` (cả hai được tính). Thu nhập danh nghĩa tháng `M ~ U[12tr; 30tr]`.
- Khoản thu dự án: mỗi tháng Poisson(2,2) khoản, ngày rút đều, mỗi khoản `LN(M/2,2; 0,6)`, người trả lấy ngẫu nhiên từ 6 tên khách hàng chung chung (`Khach hang A`…`F`).
- Định kỳ: `Tiền nhà` `M × U[0,15; 0,25]` ngày 1–5; `Internet`; `Phần mềm thiết kế` `U[250k; 500k]` ngày 12; `Cước di động`.
- Chi linh hoạt `F = M × U[0,35; 0,50]`. Nạp ví điện tử ngân hàng → ví điện tử `U[500k; 1,5tr]`, ngày 1 và 15.
- Số dư mở đầu: ngân hàng `M × U[0,5; 2,0]`, ví điện tử `U[200k; 1tr]`.

**`student`** — trợ cấp + làm thêm + học phí theo kỳ.
- Ví: `Ngân hàng` (được tính).
- `Trợ cấp gia đình` `A ~ U[3tr; 6tr]` hằng tháng, ngày gốc 1–5, lệch thêm `{−3,…,+3}` ngày (khác lương).
- `Làm thêm` hằng tuần (thứ Bảy) `W ~ U[400k; 1tr]` × `U[0,8; 1,2]`.
- Định kỳ: `Phòng trọ` `U[1tr; 2tr]` ngày 1–5; `Cước di động` `U[70k; 120k]`.
- `Học phí` **không định kỳ với mô hình**: hai khoản `U[8tr; 15tr]` vào 2025-08-25 và 2026-01-20, mỗi ngày lệch `{−5,…,+5}`, danh mục `education`.
- Chi linh hoạt `F = (A + 4,3 W) × U[0,60; 0,80]`, tỉ trọng `food` 55%, `transport` 10%, `shopping` 15%, `entertainment` 15%, `health` 2%, `other` 3%.
- Số dư mở đầu `A × U[0,3; 1,5]`.

**`family`** — nhiều ví, vay, quỹ nhóm, tiết kiệm.
- Ví: `Ngân hàng chính` (được tính), `Ví điện tử` (được tính), `Tiết kiệm` (**không** được tính, `IncludeInForecast = false`).
- Hai nguồn lương vào ngân hàng chính: `Lương 1` `U[12tr; 25tr]` ngày 5, `Lương 2` `U[8tr; 15tr]` ngày 10.
- Định kỳ:
  - `Trả góp nhà` `U[4tr; 8tr]` ngày 15;
  - `Học phí con` `U[2tr; 5tr]` ngày 5;
  - `Điện lực` gốc `U[500k; 1,2tr]`;
  - `Nước sạch` gốc `U[150k; 350k]`;
  - `Internet`;
  - `Gửi tiết kiệm` `U[2tr; 5tr]` ngày 6: chuyển ngân hàng chính → Tiết kiệm, vượt ranh giới;
  - `Quỹ gia đình` `U[500k; 1tr]` ngày 5: góp quỹ nhóm, vượt ranh giới.
- Chi linh hoạt `F = (Lương 1 + Lương 2) × U[0,35; 0,45]`, tỉ trọng `food` 40%, `transport` 12%, `shopping` 18%, `family` 15%, `entertainment` 8%, `health` 5%, `other` 2%.
- **Vay:**
  - **Đi vay** `U[20tr; 50tr]` từ "Người thân" ngày 2025-03-10 (thu `loan`), hạn 2025-12-15. Trả `U[2tr; 5tr]` ngày 20 hằng tháng từ 2025-04. Phần còn lại trả ngày hạn + `{0,…,10}` ngày. Mỗi lần trả là một `Debit` danh mục `loan`, có dòng `loan_repayments` tương ứng.
  - **Cho vay** `U[3tr; 8tr]` cho "Bạn" ngày 2025-08-01, hạn 2025-10-01, được trả đủ vào 2025-11-10 (trễ).
- Nạp ví điện tử `U[1tr; 2tr]` ngày 1 và 15 (hai ví cùng được tính).
- Số dư mở đầu: ngân hàng chính `(Lương 1 + Lương 2) × U[0,5; 1,5]`, ví điện tử `U[300k; 1tr]`, Tiết kiệm `U[20tr; 100tr]`.

**`small_trader`** — tiểu thương, thu hằng ngày, không lương.
- Ví: `Ngân hàng` và `Tiền mặt` (được tính). Doanh thu danh nghĩa ngày `R ~ U[800k; 2,5tr]`.
- Mỗi ngày mở hàng (trừ một ngày nghỉ trong tuần, rút một lần theo người): `Poisson(2) + 1` khoản thu bán hàng. Tổng ngày ≈ `R × hệ số thứ` (CN ×1,3), mỗi khoản `LN(R/3; 0,5)`, người trả là `Khach le`. 60% vào tiền mặt, 40% vào ngân hàng (chuyển khoản QR).
- `Nhập hàng` mỗi thứ Hai: `Đại lý` × `R × 7 × U[0,55; 0,70]` × `U[0,85; 1,15]` mỗi tuần. Số tiền dao động quá dung sai 10%, nên **thường không** được nhận là định kỳ; đáp án vẫn ghi là khoản tuần, có cờ "số tiền dao động".
- Định kỳ: `Thuê sạp` `U[1,5tr; 4tr]` ngày 1–5; `Điện lực`; `Cước di động`.
- Chi cá nhân linh hoạt `F = R × 30 × U[0,15; 0,25]`.
- Nộp tiền mặt vào ngân hàng mỗi thứ Bảy: 70% số tiền mặt vượt 2tr (hai ví cùng được tính).
- Số dư mở đầu: ngân hàng `R × U[5; 20]`, tiền mặt `U[1tr; 3tr]`.

### 5.4 Ngân sách và ngưỡng (giá trị người dùng tự đặt)

- **Ngân sách theo danh mục:** 60% người gốc có đặt (rút theo người). Hạn mức danh mục = `F × tỉ trọng × U[0,8; 1,3]`, làm tròn 100.000đ. Đặt từ ngày đầu tiên dùng app; K6 thì từ ngày bắt đầu.
- **Ngưỡng số dư thấp (S2):** production không có giá trị mặc định (`users.low_balance_threshold_cents` null = tắt). Bộ sinh đặt 20% thu nhập tháng danh nghĩa, làm tròn 100.000đ:
  - `stable_worker`: S;
  - `freelancer`: M;
  - `student`: A + 4,3W;
  - `family`: Lương 1 + Lương 2;
  - `small_trader`: 30R × 0,3 (biên lợi nhuận danh nghĩa 30%).

  Dùng từ tầng 2.
- **Thu nhập tháng khai báo** (`users.monthly_income_cents`): bằng thu nhập danh nghĩa ở trên. Chỉ ảnh hưởng `reserve` của "số tiền có thể chi", không ảnh hưởng dòng tiền.

## 6. Kịch bản (lớp phủ lên người gốc)

- Mỗi kịch bản Kx là **lớp phủ** lên thế giới K0 của cùng người gốc, dùng luồng riêng `scenario.Kx`. Ngoài phần được cài, mọi giao dịch giống K0 từng đồng.
- Ngày cài rút đều trong giai đoạn đánh giá, trừ khi ghi khác.
- **Cửa sổ ảnh hưởng** của một mốc T ở tầm h: `[T, T+h]` có chứa một ngày sự kiện của kịch bản. Báo cáo in cả "toàn năm" và "trong cửa sổ ảnh hưởng".

| Mã | Tầng | Lớp phủ | Ngày sự kiện (để tính cửa sổ ảnh hưởng) |
|---|---|---|---|
| K0 | 1 | Không đổi gì | — |
| K1 | 1 | Hai khoản chi đột xuất `U[3tr; 10tr]` (làm tròn 100.000đ), danh mục rút từ {`health`, `other`, `shopping`}, nơi chi `Benh vien` / `Sua chua` / `Dien may`, ngày rút đều, cách nhau ≥ 60 ngày. Đáp án: bất thường `spike`. | hai ngày đó |
| K2 | 1 | Chọn khoản thu định kỳ lớn nhất (lương, trợ cấp; `freelancer`, `small_trader` không có nên lấy thu dự án / doanh thu). Tháng M (rút trong 2025-08 → 2026-03): khoản tháng đó về trễ `{3,…,10}` ngày (với `freelancer`: mọi khoản thu dự án của tháng M dời 7–14 ngày; `small_trader`: doanh thu ngân hàng 10 ngày của tháng M dồn về một khoản cuối kỳ). Từ tháng M+2: số tiền đổi `±U[10%; 30%]` (dấu rút đều) vĩnh viễn. | ngày đến hạn tháng M; ngày đổi tiền đầu tiên |
| K3 | 1 | Chọn 1–2 khoản chi định kỳ hằng tháng (không phải lương). Mỗi khoản bỏ một kỳ (tháng rút trong 2025-08 → 2026-04). 50% số khoản bỏ kỳ thì trả gộp hai kỳ vào kỳ sau. | ngày đến hạn của kỳ bị bỏ |
| K4 | 1 | `Internet` (hoặc `Cước di động` nếu không có) tăng **+5%** (trong dung sai) từ tháng M1. Một khoản cố định khác (ưu tiên `Phần mềm thiết kế`, `Tiền nhà`, `Phòng trọ`, `Thuê sạp`, `Trả góp nhà`) tăng **+25%** (ngoài dung sai) từ tháng M2. M1, M2 rút trong 2025-08 → 2026-04. | ngày trả đầu tiên với giá mới |
| K5 | 1 | Tết 2026: chi linh hoạt nhân `U[1,5; 2,5]` (theo người) từ 2026-02-03 (mùng 1 − 14) tới 2026-02-24 (mùng 1 + 7). `stable_worker`, `family`: thưởng Tết = 1 tháng lương (mỗi nguồn), trả `{3,…,10}` ngày trước mùng 1. `small_trader`: nghỉ bán từ mùng 1 tới mùng 4, doanh thu ×1,5 trong 10 ngày trước mùng 1. `student`: thêm khoản thu `Li xi` `U[1tr; 3tr]` vào mùng 1–3. | 2026-02-03 → 2026-02-24 |
| K5h | 1 | Cùng dữ liệu K5, chạy với `forecast.holidays` = 2026-02-10 → 2026-02-24 (mùng 1 ± 7), hệ số **150%**, chọn trước (D17), khác dải 1,5–2,5 của bộ sinh. | như K5 |
| K6 | 1 | Người mới: ngày bắt đầu B rút đều trong 2025-07-15 → 2026-04-15. Bỏ mọi giao dịch trước B. Số dư mở đầu tại B = số dư của thế giới K0 tại B (từng ví). Khoản vay đã mở trước B: bỏ (người mới chưa ghi sổ vay). Chỉ chấm các mốc trong [B+14, B+75]. | mọi mốc được chấm |
| K7 | 2 | Ba giao dịch trùng: chọn ba khoản chi linh hoạt ≥ 200.000đ, thêm bản sao cùng ví, số tiền, nơi chi, sau `{1,…,8}` phút. Đáp án: bất thường `duplicate`. | ngày của bản sao |
| K8 | 2 | Đổi thói quen: từ ngày rút trong 2025-09 → 2026-04, chi linh hoạt nhân `0,7` hoặc `1,3` (dấu rút đều) vĩnh viễn. | ngày đổi |
| K9 | 2 | Định kỳ mô hình không hỗ trợ: thêm khoản 2 tuần `Phi gui xe thang` `U[300k; 800k]` và khoản hằng quý `Bao hiem` `U[1tr; 3tr]`, cả hai từ 2025-01. Đáp án: chu kỳ `biweekly` / `quarterly`. | các ngày trả |

## 7. Đáp án ghi lại

Với mỗi người × kịch bản, bộ sinh ghi các đáp án sau:

- Danh sách khoản định kỳ thật: tên chuẩn, `CounterpartyKey`, chiều, chu kỳ, ngày gốc, số tiền danh nghĩa từng kỳ, ngày bắt đầu/kết thúc, kỳ bị bỏ, lần đổi giá, cờ "số tiền dao động".
- Với mỗi giao dịch: nhãn `flexible` / `recurring:{khoản}` / `recurring_volatile:{khoản}` (khoản có cờ "số tiền dao động") / `income_irregular` / `transfer` / `loan` / `refund` / `fund` / `scenario:{Kx}`, và cờ `injected_anomaly` (K1, K7).
- Ngày sự kiện của kịch bản, ngưỡng số dư thấp, ngân sách.

**Chi linh hoạt thực tế** dùng để chấm = tổng `Debit` không có nhãn `recurring:*`, `loan`, `transfer`, `fund`, đã trừ hoàn tiền bằng `RefundNetting.ForHabits` trên cửa sổ. Nói cách khác, mọi khoản chi mà mô hình phải đoán bằng thói quen đều tính là linh hoạt: chi hằng ngày, K1, học phí sinh viên, và chi `recurring_volatile:*` như nhập hàng của tiểu thương (khoản dao động, không có trong chế độ đáp án nên mô hình học nó qua thói quen). Khoản thu dự án và bán hàng có nhãn `income_irregular`.

**Cờ "số tiền dao động"** gắn cho: `Làm thêm` (student) và `Nhập hàng` (small_trader). Điện và nước **không** gắn cờ này: người dùng vẫn khai được chúng là hóa đơn hằng tháng, và production coi lần lệch giá là lần trả với giá mới.

## 8. Chế độ khoản định kỳ cho mô hình

- **Đáp án:** chỉ dùng thông tin trước thời điểm mốc. Mỗi khoản định kỳ thật có chu kỳ `Weekly` / `Monthly`, đã bắt đầu, chưa kết thúc trước mốc, và không có cờ "số tiền dao động", được đưa vào như khoản người dùng tự khai (`Source = Manual`, `Status = Active`). Các trường:
  - `NextDueDate`: kỳ danh nghĩa ngay sau kỳ gần nhất đã có thanh toán trước mốc; chưa có kỳ nào thì là kỳ danh nghĩa đầu tiên.
  - `AmountCents`: số tiền của lần thanh toán gần nhất trước mốc; chưa có thì số danh nghĩa kỳ đầu.
  - `AnchorDay`: ngày gốc.
  - Khoản `biweekly` / `quarterly` (K9) và khoản dao động **không** có trong chế độ đáp án, vì người dùng không khai được bằng các chu kỳ production hỗ trợ.
- **Phát lại** (tầng 2): không có khoản khai tay. Mọi khoản định kỳ đến từ `RecurringDetectionService` thật chạy mỗi ngày lúc 01:30, và `ForecastAlertService.RunDailyAsync` lúc 08:00.

## 9. Baseline (chọn trước)

- **B-mean30:** cửa sổ **30 ngày** trước mốc.
- **B-naive:** lặp lại dòng tiền ròng của `h + 1` ngày ngay trước mốc (cùng số ngày với đoạn dự báo `[T, T+h]`).
- **B-lastmonth** (tầng 3): tổng chi của tháng dương lịch trước.

## 10. Lịch sử phiên bản

| Phiên bản | Ngày | Thay đổi | Lý do |
|---|---|---|---|
| 1.0 | 2026-11-27 | Bản đầu | — |
| 1.1 | 2026-11-27 | §5.1: thêm λ/σ của `family`, tỉ trọng mặc định, ví trả chi linh hoạt, hệ số nhân vào λ hay số tiền, luồng theo ngày | Thiếu sót phát hiện khi viết bộ sinh (Task 3), **trước** khi có runner hay số đo nào; v1.0 không đủ để code làm đúng một cách duy nhất |
