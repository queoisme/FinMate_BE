# Flow 6 — “Mua món này có ổn không?”

**Trạng thái:** Backend (M1–M3, phần tính toán) đã triển khai ngày 2026-11-07, xem §13. Ý định chat ở AI service (`evaluate_purchase`) cũng đã có; app Flutter chưa làm  
**Ngày:** 2026-11-06  
**Phạm vi:** Ứng dụng Flutter, Backend API, AI Service và các nguồn dữ liệu tài chính hiện có

## 1. Mục tiêu

Giúp người dùng cân nhắc **trước khi mua** một món hàng. FinMate hiểu câu hỏi, tính tác động của khoản mua lên dòng tiền từ hôm nay tới kỳ thu nhập kế tiếp, ngân sách và mục tiêu, rồi trình bày các lựa chọn dễ hiểu. Người dùng luôn là người quyết định.

**Kết quả mong muốn:** Một câu hỏi như “Hôm nay mua cái quần 800.000đ được không?” tạo ra một thẻ đánh giá có kết luận, cách tính, các rủi ro, phương án thay thế và hành động tiếp theo. Việc hỏi và mô phỏng không tạo giao dịch thực.

### Ngoài phạm vi phiên bản đầu

- Tự động thanh toán, đặt hàng, chuyển tiền hoặc chặn giao dịch.
- Khẳng định nguyên nhân tâm lý của một khoản mua, ví dụ “do TikTok”, khi không có dữ liệu do người dùng cung cấp.
- Tư vấn đầu tư, tín dụng hoặc pháp lý; chấm điểm đạo đức của khoản chi.
- Tự động dùng một số dư hoặc dự báo thiếu tin cậy để kết luận chắc chắn “mua được”.

## 2. Hiện trạng cần tận dụng và khoảng trống

| Thành phần | Hiện có | Cần cho flow mới |
|---|---|---|
| Chat văn bản/giọng nói | FE gọi `/transactions/chat`, `/transactions/voice`; có đường phân tích `/chat/analyze` | Nhận diện ý định **đánh giá khoản mua**, hỏi bổ sung, giữ ngữ cảnh của phiên mô phỏng |
| Tiền khả dụng | `/me/spendable` tính `thu nhập tháng − đã chi − tiền dành cho mục tiêu` | Tính theo **dòng tiền từng ngày**, tài khoản thực tế, khoản phải trả và mốc kỳ thu nhập; không thay nghĩa API cũ âm thầm |
| Báo cáo | `/reports/forecast` ngoại suy chi tiêu cuối tháng | Dùng làm tín hiệu tham khảo, không xem dự báo chi tiêu là hóa đơn chắc chắn |
| Ngân sách/mục tiêu | Có hạn mức, mức đã chi, mục tiêu và khoản dành riêng | Mô phỏng tác động của khoản mua vào đúng danh mục và cách quản lý tài chính hiện hành |
| Giao dịch | Có giao dịch thủ công, thông báo, OCR, QR và chuyển khoản nội bộ | Đối chiếu khoản mua thực tế với quyết định đã lưu mà không ghi trùng |
| FE assistant | Có thẻ giao dịch, tổng kết và mục tiêu; lịch sử chat chủ yếu trong phiên | Thêm thẻ đánh giá quyết định, so sánh kịch bản, giải thích con số |

**Lưu ý cốt lõi:** `SpendableCents` hiện tại là số còn lại trong *kế hoạch tháng*, không phải tiền mặt chắc chắn còn có thể tiêu *từ hôm nay đến kỳ lương*. Hai chỉ số phải có tên và cách giải thích riêng.

## 3. Actor và thành phần tham gia

### Actor nghiệp vụ: 2

1. **Người dùng FinMate (chính):** khởi tạo câu hỏi, cung cấp/sửa dữ liệu, xem mô phỏng, chọn hành động và xác nhận mọi thay đổi dữ liệu.
2. **Bộ lập lịch FinMate (phụ, chỉ ở nhánh nhắc lại):** đến thời điểm người dùng đã chọn thì khởi tạo thông báo nhắc; không tự đưa quyết định thay người dùng.

### Thành phần hệ thống hỗ trợ: 4

1. **Flutter app:** chat, nhập văn bản/giọng nói, hiển thị thẻ, giữ ngữ cảnh kịch bản, mở màn giải thích và xử lý thao tác.
2. **Backend API:** xác thực, đọc dữ liệu qua repository, tính dòng tiền và tác động, kiểm tra điều kiện, lưu lựa chọn khi được yêu cầu.
3. **AI Service:** nhận diện ý định và thông tin món hàng; có thể diễn giải kết quả có cấu trúc. AI không tự tính số dư hoặc sửa dữ liệu.
4. **STT Service (tùy chọn):** chuyển giọng nói thành chữ; người dùng được xem/sửa bản chép trước khi mô phỏng.

Không coi ngân hàng, sàn thương mại điện tử hay nhà cung cấp AI bên ngoài là actor bắt buộc của flow. Phiên bản đầu chỉ dùng dữ liệu FinMate đã có và thông tin người dùng nhập.

## 4. Điều kiện bắt đầu và kết thúc

### Cách bắt đầu

- **Từ chat:** người dùng gõ/nói “Mua [món] [giá] hôm nay có ổn không?”.
- **Từ Trang chủ:** bấm “Thử một khoản mua”, sau đó nhập món và giá.
- **Từ danh sách muốn mua (giai đoạn sau):** mở một món đã lưu và bấm “Đánh giá lại”.

Điều kiện nền: đã đăng nhập; dữ liệu tài chính chỉ được lấy theo `userId` của phiên. Người dùng có thể bắt đầu ngay cả khi chưa khai thu nhập hoặc thiếu lịch hóa đơn; hệ thống chuyển sang kết quả *chưa đủ dữ liệu* thay vì đoán một con số chắc chắn.

### Cách kết thúc

Một phiên đánh giá kết thúc khi người dùng: (a) xem xong và đóng; (b) thử kịch bản khác rồi đóng; (c) lưu món/đặt nhắc; hoặc (d) xác nhận tạo một mục tiêu hay thay đổi kế hoạch ở flow tương ứng. Đánh giá không tự biến thành giao dịch. Nếu có giao dịch thực về sau, đó là một sự kiện mới để đối chiếu.

## 5. Input và output

### Input trực tiếp

| Trường | Bắt buộc | Nguồn | Quy tắc |
|---|---|---|---|
| Món hoặc mục đích mua | Có để giải thích; có thể dùng nhãn “Khoản mua” | Câu chat/form | Không suy đoán nhu cầu thiết yếu chỉ từ tên món |
| Giá VND | Có | Câu chat/form | Số nguyên dương, đơn vị đồng; không dùng `float`/`decimal` để tính tiền |
| Thời điểm mua | Không | Câu chat/form | Mặc định hôm nay theo múi giờ Việt Nam; cho thử ngày khác |
| Danh mục | Có thể thiếu | AI gợi ý/người dùng chọn | Thiếu thì hỏi hoặc tính tổng tiền nhưng không khẳng định tác động vào một ngân sách cụ thể |
| Tài khoản chi | Có thể thiếu | Người dùng chọn | Thiếu thì không khẳng định tài khoản nào đủ tiền |
| Phương thức trả | Không | Người dùng chọn | V1 là trả toàn bộ một lần; trả góp là nhánh mở rộng cần kỳ hạn, phí/lãi và lịch thu nợ |

### Input hệ thống

Số dư tài khoản được chọn và thời điểm cập nhật; giao dịch đã xác nhận và giao dịch chờ; thu nhập và ngày nhận dự kiến; hóa đơn/khoản định kỳ đã xác nhận; mức chi thiết yếu dự kiến; ngân sách còn lại; các mục tiêu và khoản dành riêng; múi giờ; các dữ liệu bị thiếu hoặc đã cũ. **Không cộng** chuyển khoản nội bộ vào thu nhập/chi tiêu và **không trừ hai lần** một hóa đơn đã có giao dịch thanh toán.

### Output

1. **Trạng thái:** `assessable`, `partial` hoặc `insufficient_data` (tên đề xuất, chưa phải API contract).
2. **Kết luận có điều kiện:** “phù hợp với kế hoạch”, “cần cân nhắc”, “có nguy cơ thiếu tiền”, hoặc “chưa đủ dữ liệu”. Tránh nhị phân “được/không được”.
3. **Số liệu trước/sau mua:** tiền linh hoạt và **điểm số dư thấp nhất** trong kỳ tính; tác động đến ngân sách và tiến độ mục tiêu.
4. **Bảng giải thích:** từng khoản cộng/trừ, ngày dự kiến, nguồn (`confirmed`, `user_entered`, `estimated`) và thời điểm dữ liệu được cập nhật.
5. **Các phương án:** mua hôm nay, đổi giá, đổi ngày; trả góp chỉ khi đã đủ dữ liệu và đã triển khai nhánh riêng.
6. **Hành động:** “Xem cách tính”, “Thử giá khác”, “Đổi ngày mua”; giai đoạn sau thêm “Lưu món”, “Nhắc lại”, “Tạo mục tiêu”.

## 6. Quy tắc tính toán

### 6.1 Kỳ tính

Mặc định từ **hôm nay đến ngay trước kỳ thu nhập kế tiếp đã được xác nhận**. Nếu không biết kỳ thu nhập, cho chọn mốc 7/14/30 ngày và ghi rõ đó là mốc do người dùng chọn. Không tự suy ra một ngày lương chắc chắn từ lịch sử biến động.

### 6.2 Sổ dòng tiền theo ngày

Tại mỗi ngày `d` trong kỳ:

```text
projectedBalance(d)
  = openingAvailableBalance
  + confirmedExpectedInflows(up to d)
  - confirmedObligations(up to d)
  - essentialLivingReserve(up to d)
  - goalReserveNotAlreadyDeducted(up to d)
  - hypotheticalPurchase(if purchaseDate <= d)
```

`minimumProjectedBalance` là giá trị nhỏ nhất của chuỗi trên. **Tiền linh hoạt tại thời điểm mua** là mức có thể chi mà vẫn không làm `minimumProjectedBalance` xuống dưới vùng đệm an toàn do người dùng chọn. Có thể hiển thị giá trị âm như “thiếu dự kiến”, không ép về 0 rồi che đi nguy cơ.

### 6.3 Các ranh giới tiền tệ

- Số tiền là `BIGINT`/`long` VND; API có thể giữ tên `amountCents` theo hợp đồng hiện tại nhưng giá trị là **đồng VND**, không chia 100.
- Số dư tài khoản có thể cũ hoặc không đầy đủ. Cần hiển thị thời điểm cập nhật và nguồn; khi không đáng tin, trạng thái là `partial`.
- Khoản dự kiến chỉ được tính một lần theo ID và loại nguồn. Nếu hóa đơn đã thanh toán, bỏ lịch phải trả tương ứng.
- Chuyển khoản nội bộ đổi nơi giữ tiền nhưng không tạo thu nhập/chi tiêu toàn hệ thống. Nếu đánh giá **một tài khoản cụ thể**, phải xét dòng tiền vào/ra của tài khoản đó.
- Khoản dành cho mục tiêu theo phương pháp `reserve_first`/`envelope`/`available_to_spend` phải tái dùng quy tắc hiện hành và tránh trừ lại tiền đã thực sự chuyển sang tài khoản khác.
- Ngân sách là **ràng buộc kế hoạch**, không phải một khoản tiền mặt bị trừ thêm khỏi số dư. Hiển thị cảnh báo vượt ngân sách riêng để tránh đếm hai lần.
- Ngưỡng “phù hợp/cân nhắc/nguy cơ” cần cấu hình và kiểm thử với dữ liệu thật; không gắn một phần trăm tùy ý trong đặc tả này.

### 6.4 Ví dụ minh họa

Ngày 10: tài khoản có 5.000.000đ; ngày 12 phải trả tiền nhà 3.000.000đ; từ ngày 10 đến 15 cần giữ 1.000.000đ sinh hoạt; ngày 15 nhận lương 10.000.000đ. Người dùng hỏi mua quần 1.500.000đ ngày 10.

- Trước mua: điểm thấp nhất trước ngày nhận lương là 1.000.000đ.
- Sau mua: điểm thấp nhất là **−500.000đ**, tức có nguy cơ thiếu 500.000đ trước ngày 15.
- Mua vào ngày 16 cho kết quả khác dù tổng tiền của cả tháng không đổi.

Ví dụ này cho thấy vì sao chỉ lấy `thu nhập tháng − chi tháng` hoặc số dư hiện tại sẽ trả lời sai câu hỏi “mua **hôm nay** có ổn không?”.

### 6.5 Kết luận dựa trên tiêu chí nào?

Đây là **đánh giá khả năng chịu khoản mua trong một kỳ thời gian**, không phải phán quyết món hàng có đáng mua hay không. Backend tạo kết luận từ bốn kiểm tra độc lập:

| Kiểm tra | Câu hỏi cần trả lời | Dữ liệu dùng |
|---|---|---|
| Thanh khoản theo ngày | Sau khi mua, có ngày nào số dư dự kiến xuống dưới 0 hoặc dưới vùng đệm người dùng đặt không? | Số dư, ngày thu nhập, lịch hóa đơn, chi thiết yếu, ngày mua |
| Ngân sách | Khoản mua có làm vượt hạn mức danh mục trong kỳ không? | Danh mục, số đã chi, hạn mức |
| Mục tiêu | Khoản mua có khiến khoản dành cho mục tiêu đã cam kết không còn khả thi không? | Mục tiêu, deadline, `GoalMode`, khoản đã nạp/dành |
| Chất lượng dữ liệu | Những đầu vào quan trọng có đầy đủ, còn mới và đã được xác nhận không? | Nguồn, thời điểm cập nhật, trạng thái xác nhận |

**Thứ tự quyết định đề xuất** (cần chốt ngưỡng cụ thể trước khi triển khai):

1. Thiếu hoặc cũ **số dư, khoản phải trả lớn, hoặc mốc thời gian** đến mức không kiểm tra được thanh khoản → `insufficient_data`/`partial`; không trả “mua ổn”.
2. Điểm thấp nhất sau mua `< 0` trước một nghĩa vụ đã biết → **“Có nguy cơ thiếu tiền”**, kèm ngày và khoản thiếu. Nếu điểm thấp nhất chỉ thấp hơn vùng đệm do người dùng đặt → **“Cần cân nhắc”**.
3. Không thiếu tiền nhưng vượt ngân sách hoặc làm chậm mục tiêu đã cam kết → **“Có thể thanh toán, nhưng lệch kế hoạch”**; nêu chính xác kế hoạch bị ảnh hưởng. Ngân sách không được trừ thêm lần nữa vào tiền mặt.
4. Không có vấn đề ở các kiểm tra trên và dữ liệu đủ tin cậy → **“Phù hợp với kế hoạch hiện tại”**, luôn kèm kỳ tính và điều kiện dữ liệu. Không dùng từ “chắc chắn” hay “nên mua”.

Mức độ cần thiết của món hàng và cảm nhận của người dùng là **thông tin chủ quan**. AI có thể hỏi “Món này phục vụ nhu cầu gì?” hoặc dùng ưu tiên do người dùng tự khai, nhưng không được biến nhãn “quần áo” thành kết luận đạo đức hay tự quyết món đó đáng mua.

### 6.6 Đánh giá có “thật” không và kiểm chứng thế nào?

**Phép tính có thể là thật theo dữ liệu đầu vào; dự báo không phải sự thật về tương lai.** Ví dụ, số dư đã xác nhận là quan sát tại một thời điểm; hóa đơn đã xác nhận là nghĩa vụ dự kiến; chi sinh hoạt và thu nhập tương lai có thể chỉ là ước tính. Kết luận đúng khi các giả định còn đúng, và cần tính lại sau giao dịch hoặc thay đổi lịch.

Thẻ đánh giá phải có phần “Vì sao?” liệt kê từng dữ kiện, nguồn, ngày cập nhật và đánh dấu `đã xác nhận`/`người dùng nhập`/`ước tính`. UI không hiện một “điểm AI” chung chung. Trạng thái tin cậy là **độ đầy đủ và độ mới của dữ liệu**, không phải xác suất thống kê rằng quyết định mua sẽ tốt. Chỉ được hiển thị xác suất khi có mô hình được hiệu chuẩn và đo trên dữ liệu thực.

Để kiểm chứng sản phẩm, cần: (a) test phép tính bằng các tình huống chuẩn và ranh giới tiền; (b) chạy lại quyết định đã lưu sau khi kỳ dự báo kết thúc, so số dư thấp nhất dự báo với số dư thực, tỷ lệ hóa đơn bị bỏ sót và số lần kết luận “phù hợp” nhưng thực tế thiếu tiền; (c) đo riêng lỗi AI hiểu sai giá/ngày/danh mục; (d) kiểm tra người dùng có hiểu điều kiện và cách tính không. Nếu tỷ lệ sai cao, hạ mức khẳng định hoặc yêu cầu thêm dữ liệu, không dùng AI viết lời tự tin hơn.

**Ví dụ câu trả lời đúng mức:** “Theo số dư cập nhật lúc 09:00 và hai hóa đơn bạn đã xác nhận, nếu mua hôm nay 800.000đ thì mức tiền thấp nhất trước ngày nhận lương là 300.000đ, thấp hơn vùng đệm 500.000đ bạn đặt. Mình đánh giá là *cần cân nhắc*. Khoản điện tháng này chưa có số tiền xác nhận, nên kết quả có thể thay đổi.”

## 7. Workflow chi tiết

### WF-01 — Bắt đầu bằng chat hoặc form

1. Người dùng nhập câu hoặc mở form “Thử một khoản mua”.
2. FE gửi ý định phân tích. Nếu là giọng nói, STT trả bản chép để người dùng kiểm tra.
3. AI Service trích `item`, `amount`, `purchaseDate`, `category`, `paymentMethod` và mức chắc chắn. Không ghi giao dịch.
4. FE hiển thị thông tin đã hiểu. Trường quan trọng mơ hồ thì hỏi một câu ngắn; người dùng sửa được.
5. Khi có giá hợp lệ và ngày mua, chuyển WF-02.

**Input:** câu/form/âm thanh. **Output:** yêu cầu mô phỏng có cấu trúc hoặc câu hỏi bổ sung. **Kết thúc sớm:** người dùng hủy; câu ngoài phạm vi được trả lời như chat thông thường.

### WF-02 — Tạo snapshot tài chính và đánh giá

1. Backend xác thực và lấy dữ liệu của đúng người dùng qua repository.
2. Xác định kỳ tính, tài khoản, số dư, khoản chắc chắn, khoản ước tính, ngân sách và mục tiêu.
3. Kiểm tra dữ liệu thiếu/cũ và chống trùng các khoản đã thanh toán.
4. Tính dòng tiền gốc, dòng tiền sau mua và điểm thấp nhất của mỗi kịch bản.
5. Tính tác động ngân sách và mục tiêu riêng; phân loại trạng thái đánh giá.
6. Trả kết quả có cấu trúc cùng bằng chứng và cảnh báo dữ liệu.

**Input:** yêu cầu mô phỏng + snapshot. **Output:** kết quả tính tiền xác định được bằng code. **Không có side effect.**

### WF-03 — Trình bày và giải thích

1. AI nhận **kết quả đã tính** và tạo lời giải thích ngắn; không sửa số liệu.
2. Backend/FE đối chiếu các số hiển thị với payload cấu trúc; nếu AI lỗi, dùng câu chữ mẫu từ BE/FE.
3. FE hiện thẻ: kết luận, số tiền trước/sau, cảnh báo quan trọng nhất, các nút hành động.
4. “Xem cách tính” mở danh sách nguồn dữ liệu và lịch dòng tiền theo ngày.

**Input:** kết quả WF-02. **Output:** thẻ tư vấn có thể kiểm chứng. **Kết thúc:** người dùng đóng hoặc chuyển WF-04/05.

### WF-04 — Thử phương án khác

1. Người dùng nói “Nếu đợi tới ngày 16?” hoặc sửa giá/ngày trên thẻ.
2. FE giữ món và các trường không đổi; chỉ gửi tham số mới.
3. Backend lấy snapshot mới hoặc xác nhận snapshot cũ còn hiệu lực, rồi tính lại.
4. FE so sánh các phương án trên cùng một mốc dữ liệu; nếu dữ liệu nền đã đổi, báo “đã cập nhật dữ liệu” và tính lại tất cả phương án.

**Input:** giá/ngày/phương án mới. **Output:** so sánh trước và sau. **Giới hạn:** không khẳng định phương án trả góp khi thiếu phí, lãi hoặc lịch kỳ trả.

### WF-05 — Lưu ý định và hành động (giai đoạn sau)

1. Người dùng chọn lưu món, đặt nhắc hoặc tạo mục tiêu.
2. FE hiện bản xem trước và yêu cầu xác nhận.
3. Backend ghi đúng đối tượng được chọn; lệnh có mã chống gửi trùng.
4. Trả trạng thái đã lưu; nếu lỗi, giữ thẻ đánh giá và cho thử lại.

**Input:** hành động được xác nhận. **Output:** món muốn mua/lịch nhắc/mục tiêu. **Không tự tạo giao dịch mua.**

### WF-06 — Nhắc lại và đánh giá lại (giai đoạn sau)

1. Bộ lập lịch đến mốc người dùng chọn; kiểm tra người dùng còn cho phép thông báo.
2. Gửi thông báo ngắn, không lộ thông tin tài chính nhạy cảm trên màn khóa theo cài đặt.
3. Khi người dùng mở, hệ thống lấy dữ liệu mới và chạy lại WF-02; không hiển thị con số cũ như hiện tại.
4. Người dùng mua, hoãn tiếp, bỏ ý định hoặc đóng.

**Input:** ý định mua + mốc nhắc. **Output:** đánh giá mới và lựa chọn mới.

### WF-07 — Đối chiếu giao dịch thực và học từ phản hồi (giai đoạn sau)

1. Có giao dịch mới từ thông báo, OCR hoặc nhập tay.
2. Hệ thống tìm ý định mua có khả năng khớp theo người dùng, thời gian, số tiền và mô tả; chỉ **gợi ý** liên kết khi chưa chắc.
3. Người dùng xác nhận liên kết; có thể đánh giá quyết định là hữu ích/không hữu ích hoặc bỏ qua.
4. FinMate cập nhật trạng thái ý định và dùng phản hồi để cải thiện cách gợi ý; không sửa giao dịch tài chính từ phản hồi này.

**Input:** giao dịch thật + phản hồi tùy chọn. **Output:** liên kết và dữ liệu học có sự đồng ý. **Tránh liên kết trùng nhiều ý định với một giao dịch.**

## 8. Nhánh ngoại lệ bắt buộc

| Tình huống | Hành vi |
|---|---|
| Thiếu giá | Hỏi giá, không mô phỏng |
| Thiếu ngày nhận thu nhập | Cho chọn mốc thời gian; không tự khẳng định ngày lương |
| Thiếu/số dư cũ | Báo chưa đủ dữ liệu hoặc đánh giá một phần; mời cập nhật |
| Không rõ danh mục | Tính tác động dòng tiền, để tác động ngân sách ở trạng thái chưa xác định |
| Chưa có ngân sách | Không mặc định ngân sách bằng 0; chỉ hiện tác động dòng tiền |
| AI hoặc mạng lỗi | Giữ câu hỏi; nếu đã có input cấu trúc, phép tính BE vẫn có thể chạy; không bịa kết quả |
| Số tiền lớn hơn mức cho phép hoặc âm | Từ chối input, yêu cầu sửa |
| Trả góp | Hỏi kỳ hạn, khoản trả trước, phí/lãi, ngày trả; nếu thiếu thì chỉ nói chưa đủ dữ liệu |
| Giao dịch/thu nhập thay đổi giữa hai lần thử | Tính lại toàn bộ phương án trên snapshot mới |
| Người dùng không đồng ý AI bên thứ ba | Dùng nhận diện/rule nội bộ hoặc form; không gửi dữ liệu sang bên thứ ba |

## 9. Phân chia trách nhiệm kỹ thuật

- **FE:** thu input, xác nhận thông tin AI hiểu, hiển thị kết quả và các trường hợp thiếu dữ liệu; không tính kết luận tiền độc lập với BE.
- **Backend Application:** điều phối use case và tính toán thuần xác định; Controller chỉ validate/map/gọi handler. Repository cung cấp snapshot, không để raw SQL trong Service.
- **AI Service:** trích ý định/thực thể và diễn giải kết quả. Chỉ nhận dữ liệu tối thiểu cần thiết, tuân thủ cờ đồng ý AI. AI DB chỉ lưu hash `SHA-256(user_id)` nếu cần dữ liệu học.
- **Persistence:** nếu cần lịch hóa đơn, ý định mua hoặc lịch nhắc, phải thiết kế schema và migration mới. Tài liệu này **chưa phê duyệt schema/API contract**.
- **Security:** tất cả endpoint người dùng cần xác thực; không log token, nội dung thông báo gốc hoặc số tiền; không đưa stack trace về client.

## 10. Gói triển khai đề xuất

| Mốc | Nội dung | Tiêu chí hoàn thành |
|---|---|---|
| M1 — Dữ liệu và lịch dòng tiền | Ngày thu nhập, khoản định kỳ do người dùng xác nhận, nguồn và độ mới dữ liệu | Xem được lịch thu/chi và sửa từng khoản; không đếm trùng |
| M2 — Bộ mô phỏng | Snapshot, dòng tiền từng ngày, ảnh hưởng ngân sách/mục tiêu, trạng thái thiếu dữ liệu | Cùng input cho cùng kết quả; kiểm thử các ca ở §11 |
| M3 — Chat và UI | Nhận diện câu hỏi, hỏi bổ sung, thẻ kết quả, xem cách tính, thử giá/ngày khác | Đi được trọn flow từ câu hỏi tới so sánh mà không ghi giao dịch |
| M4 — Theo dõi quyết định | Danh sách muốn mua, nhắc lại, liên kết giao dịch, phản hồi | Chỉ ghi sau xác nhận; nhắc mở lại bằng dữ liệu mới |

M1–M3 là bản flow đầu tiên có thể dùng và demo. M4 có thể phát triển sau khi đo độ chính xác và mức sử dụng.

## 11. Các tình huống kiểm thử chấp nhận

1. Hóa đơn đến **trước** kỳ lương: tổng tháng dương nhưng ngày mua gây thiếu tiền.
2. Đổi ngày mua sang **sau** kỳ lương: kết quả đổi đúng.
3. Hóa đơn đã thanh toán: không trừ lần hai.
4. Chuyển khoản giữa hai tài khoản của cùng người dùng: không tính là chi tiêu; khi chọn tài khoản nguồn, số dư nguồn vẫn giảm.
5. Mục tiêu đã được góp tiền: quy tắc `GoalMode` không giữ lại cùng khoản hai lần.
6. Thiếu thu nhập, số dư hoặc danh mục: trả trạng thái phù hợp, không bịa con số.
7. Giao dịch hoàn tiền và giao dịch trùng: snapshot không cộng/trừ sai.
8. Hai người dùng có dữ liệu tương tự: không rò dữ liệu chéo.
9. AI trả số khác payload tính toán: FE/BE dùng số từ bộ tính, không dùng số AI tự viết.
10. Nhắc lại sau một giao dịch mới: kết quả được tính lại.

## 12. Quyết định cần chốt trước khi triển khai

1. **Nguồn số dư:** dùng số dư do ngân hàng thông báo, số dư BE tổng hợp hay cho người dùng chọn tài khoản đáng tin cậy? Cần một chính sách khi các nguồn lệch nhau.
2. **Khoản thiết yếu và vùng đệm:** người dùng tự đặt, suy từ lịch sử rồi xác nhận, hay lấy từ phương pháp tài chính đang dùng?
3. **Thu nhập thất thường:** mốc thời gian và mức thu nhập dự kiến được xác nhận như thế nào?
4. **Lưu dữ liệu:** M1/M4 cần bảng nào, giữ trong bao lâu và người dùng xóa như thế nào?
5. **API contract:** tạo route mô phỏng mới hay mở rộng route chat hiện tại? Nên giữ phép tính là một use case đọc độc lập để FE form cũng gọi được.
6. **Ngưỡng kết luận:** khi nào “phù hợp”, “cân nhắc”, “nguy cơ”; nên thử với bộ kịch bản thực tế trước khi chốt.

Các quyết định thay đổi schema, dependency, hợp đồng BE–AI hoặc edge case tiền/bảo mật cần chủ dự án duyệt theo `AGENTS.md` trước khi viết code. Tài liệu này là bản đặc tả để review các quyết định đó.

## 13. Đã triển khai (2026-11-07)

Bản backend của team **đã có** engine dự báo dòng tiền (Phase 54–56: `CashflowEngine`, kịch bản, khoản định kỳ, ngân sách, mục tiêu), nên §2 ở trên đã cũ: M1 và M2 không cần bảng hay migration mới. Chức năng này là một lớp mỏng trên engine đó. Trong code gọi là *purchase check* để không trùng tên "Flow 6" của dự báo tài chính.

**API:** `POST /api/v1/forecast/purchase-check` (chỉ đọc; JWT; không tạo giao dịch). Request: `amountCents` (đồng VND, 1…100 tỷ), `purchaseDate?` (mặc định hôm nay, tối đa `forecast.maxHorizonDays`), `item?` (≤ 60 ký tự), `categoryId?` hoặc `categorySlug?`, `accountId?` (một ví của chính user), `horizonDays?` (chỉ dùng khi chưa biết kỳ thu nhập). Sai input → 422 `PURCHASE_CHECK_INVALID`; ví của người khác → 404.

**Cách tính** (`PurchaseCheckService` + hàm thuần `PurchaseVerdict`):
1. Chạy `CashflowEngine` hai lần, không mua và có khoản mua giả định (`ScenarioItem`), cùng cửa sổ `maxHorizonDays`.
2. Kỳ tính: từ hôm nay tới ngày trước kỳ thu nhập kế tiếp **sau** ngày mua (`basis = next_income`). Chưa biết kỳ thu nhập: dùng `horizonDays` do người dùng chọn (`user_chosen`), không có nữa thì `forecast.purchaseDefaultHorizonDays` (`default_horizon`, kết quả chỉ ở mức một phần). Không tự suy ra ngày lương.
3. `verdict`: `shortfall_risk` (điểm thấp nhất sau mua < 0, luôn được báo kể cả khi dữ liệu chưa đủ) → `consider` (thấp hơn vùng đệm `lowBalanceThresholdCents` do người dùng đặt) → `insufficient_data` (dữ liệu chỉ ở mức một phần, không bao giờ trả "phù hợp") → `off_plan` (đủ tiền nhưng vượt ngân sách danh mục/tổng hoặc làm số còn lại của kế hoạch tháng `/me/spendable` âm) → `fits_plan`. Không có ví nào tính vào dự báo → `status = insufficient_data`, không có số liệu.
4. `status`: `assessable`, hoặc `partial` khi độ tin cậy thấp (`confidence = low`), chưa biết kỳ thu nhập mà người dùng cũng không chọn mốc, hoặc số dư cũ quá `forecast.purchaseStaleBalanceDays`.
5. Ngân sách chỉ là ràng buộc kế hoạch, không trừ thêm vào tiền mặt; không có danh mục/ngân sách thì `unknown`, không coi bằng 0.
6. Phương án: `maxAffordableCents` (giá cao nhất mua đúng ngày đó mà không xuống dưới vùng đệm) và `earliestSafeDate` (chỉ khi `shortfall_risk`/`consider`). `lines` là bảng giải thích với nguồn `confirmed` / `user_entered` / `estimated`; `warnings` có mã cố định; `summary` là câu mẫu do backend sinh, **không gọi AI** nên AI hay mạng lỗi vẫn có câu trả lời, và dữ liệu tài chính không rời hệ thống.

**Cấu hình admin** (nhóm `forecast`, không đổi schema): `purchaseDefaultHorizonDays` = 14, `purchaseStaleBalanceDays` = 7. Ngưỡng "cân nhắc" lấy từ vùng đệm của chính người dùng nên không có phần trăm tùy ý.

**Giới hạn đã biết:** chưa có cột `balance_updated_at`, độ mới số dư lấy từ `financial_accounts.updated_at`; hai lần chạy engine không cùng một snapshot tuyệt đối; người dùng mới (< `minHistoryDays` ngày dữ liệu) luôn ở mức `partial`; trả góp chưa hỗ trợ. M4 (lưu món, nhắc lại, đối chiếu giao dịch) và diễn giải bằng AI chưa làm.

**Chat:** `ChatParseResponse` có thêm `purchase` (`item`, `amount_cents`, `purchase_date`, `category_slug`) và `ChatActions.Purchase = "purchase"`: `/chat/analyze` và `/transactions/chat` chỉ trả tham số, app gọi `purchase-check` để lấy đánh giá. Phần nhận diện câu hỏi nằm ở AI service (ý định `evaluate_purchase`).
