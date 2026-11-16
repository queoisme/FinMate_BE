# Nạp tri thức cho AI Financial Agent (RAG)

Thư mục này gom nội dung nháp và cách nộp. **Chưa có gì được gửi lên hệ thống**; mọi tài liệu đều phải qua bước duyệt của người trước khi agent dùng. Danh sách chủ đề lấy từ 40 câu hỏi kiến thức `kn-01…kn-40` của bộ đánh giá (`ai-service/agent_eval/dataset/questions.jsonl`), nên nạp đủ là có thể đo `retrieval_recall_at_5`. Hợp đồng và danh sách đầy đủ: `docs/agent-content-checklist.md`.

| Nhóm | Nội dung | Nguồn | Trạng thái |
|---|---|---|---|
| A | 25 tài liệu của riêng FinMate: 13 phương pháp, cảnh báo T1/T2, mục tiêu, tiền khả dụng, dải dự báo, độ tin cậy... | chính app (`group-a-finmate.json`) | Đã soạn, chờ bạn đối chiếu với app rồi nộp |
| B | Kiến thức tài chính chung (quỹ khẩn cấp, trả nợ, thuế...) | cần trang nguồn cụ thể | 8 tài liệu (4 quỹ khẩn cấp đã duyệt; 4 mới ngày 2026-11-11 đang ở trạng thái nháp chờ duyệt: tỉ lệ nợ trên thu nhập, lãi kép, hai cách trả nợ, mục tiêu ngắn và dài hạn, đều từ trang CFPB đã mở và đối chiếu). **Chưa soạn** vì chưa mở được trang nguồn chính thức: thuế TNCN và bảo hiểm y tế (luật đang đổi, kết quả tìm được toàn trang thứ cấp, số liệu mâu thuẫn nhau), lạm phát, chi tiêu cố định/biến đổi (trang CFPB chỉ có mục tiêu bài học), học phí/Tết, nợ hay tiết kiệm trước, lãi vay tiêu dùng, ngân sách cuốn chiếu, chi phí cơ hội, chi tiêu quá tay, cập nhật ngân sách |
| C | Giả định kinh tế (lạm phát, giá nhà, giá xe, lương, lãi suất) | cần trang nguồn cụ thể | Danh sách (mục 5), người nhập ở trang *Giả định kinh tế* |

## 1. Cách nộp nhóm A

1. **Đối chiếu trước.** `group-a-finmate.json` soạn từ mã và dữ liệu khởi tạo của app ngày 2026-11-09 (`FinanceMethodSeeder.cs`, các lớp tính mục tiêu, tiền khả dụng, dải dự báo). Admin có thể đã sửa nội dung hoặc tham số mặc định của phương pháp trong DB; số trong tài liệu (ví dụ 50/30/20, T1 = 70%) phải khớp với app đang chạy. Sai chỗ nào thì sửa trong tệp JSON.
2. **Kiểm tra, chưa gửi gì:**
   ```bash
   python scripts/submit_knowledge_drafts.py docs/knowledge-drafts/group-a-finmate.json
   ```
   Script kiểm giới hạn độ dài, ngày giờ, trường lạ, trùng tiêu đề, và cảnh báo nếu văn bản chứa số tiền cụ thể (xem mục 2).
3. **Gửi bản nháp** (tạo `Draft`, không duyệt):
   ```bash
   FINMATE_ADMIN_TOKEN=<token đăng nhập admin> python scripts/submit_knowledge_drafts.py docs/knowledge-drafts/group-a-finmate.json --apply --base-url http://localhost:8080
   ```
   Token là chuỗi truy cập khi đăng nhập tài khoản admin/staff; script không nhận mật khẩu và không in token. Nếu API từ chối (401/403), nhập tay ở trang *Tri thức AI* của web quản trị (nút Thêm tài liệu). Chạy lại không tạo bản trùng: tài liệu cùng tiêu đề (mọi trạng thái) bị bỏ qua.
4. **Duyệt.** OpsManager hoặc Admin vào web quản trị → *Tri thức AI*, bấm *Đọc nội dung* để đọc từng tài liệu rồi *Duyệt*. Chỉ bản `Approved`, có `effective_from` không ở tương lai, agent mới tìm thấy.
5. **Sửa nội dung** = gửi bản mới (ví dụ đổi `(v1)` thành `(v2)` ở tiêu đề), duyệt, rồi **Gỡ** bản cũ. Không có sửa tại chỗ.

Môi trường dev dùng chung đang chạy; chỉ nạp vào đó khi bạn thấy phù hợp. Nạp vào một môi trường thử trước cũng được.

## 2. Quy tắc viết (áp dụng cho cả nhóm A, B)

- **Không viết số tiền cụ thể.** Bước kiểm chứng của agent chỉ cho số tiền trong câu trả lời khi nó trùng giá trị một bằng chứng; đoạn tài liệu là chữ, không có giá trị tiền. Viết "20% thu nhập", "3–6 tháng chi tiêu", "T1 là 70% hạn mức" thì được (phần trăm, số tháng, năm, ngày không bị tính là tiền). Viết "giảm trừ 11 triệu" thì agent không trích lại được.
- **Một chủ đề một tài liệu; câu ngắn, tự đứng được.** Hệ thống cắt tài liệu thành đoạn tối đa 450 ký tự ở ranh giới câu, và mỗi đoạn được tìm, trích dẫn riêng. Đừng viết "như trên", "nó" để chỉ sang câu khác.
- **Từ khóa ở tiêu đề và câu đầu**: "quỹ khẩn cấp", "50/30/20", "tuyết lăn"... để cả tìm theo ngữ nghĩa lẫn theo từ khóa đều trúng.
- **Tiếng Việt có dấu.** Mỗi tài liệu tối đa 50.000 ký tự, thực tế nên 300–800 ký tự.
- **Mỗi con số pháp lý hay lãi suất phải mở trang nguồn kiểm tra**, ghi `source_url` là trang cụ thể (không phải trang chủ), `as_of` là ngày của số liệu. Không điền theo trí nhớ.
- Không nạp khuyến nghị mua cổ phiếu, tiền mã hóa, vàng hay sản phẩm đầu tư cụ thể; agent từ chối loại câu này theo thiết kế.

## 3. Nhóm A đã phủ những câu hỏi nào

`kn-02, 03, 05, 06, 07, 10, 20, 28, 29, 31, 33, 34` (phương pháp, T1/T2, mục tiêu, dải dự báo, độ tin cậy, số dư thấp nhất) và một phần `kn-13, 40` (xem mục 4). Sau khi duyệt, gắn `expect.relevant_sources` cho các câu này bằng đúng tiêu đề tài liệu.

## 4. Nhóm B: dàn ý (người soạn, mỗi tài liệu một nguồn)

Mỗi dòng là một tài liệu đề xuất. "Ý cần nêu" là khung nội dung, không phải số liệu. "Nguồn gợi ý" là loại trang để bạn tìm; hãy mở và đọc trang thật trước khi viết.

| Tiêu đề đề xuất | Câu hỏi bộ đánh giá | Ý cần nêu | Nguồn gợi ý |
|---|---|---|---|
| Quỹ khẩn cấp nên bằng bao nhiêu tháng chi tiêu | kn-04 | khái niệm, vì sao có, số tháng khuyến nghị theo nhóm người (lương cố định, thu nhập thay đổi), nên tăng dần | tài liệu giáo dục tài chính của cơ quan quản lý hoặc tổ chức bảo vệ người tiêu dùng tài chính |
| Quỹ khẩn cấp nên giữ ở đâu | kn-16, kn-39 | tiêu chí an toàn, dễ rút, ít biến động; so sánh tiền mặt, tài khoản thanh toán, tiết kiệm; không khuyến nghị sản phẩm cụ thể | như trên; thông tin chung của Ngân hàng Nhà nước về tiền gửi |
| Thu nhập không ổn định thì lập ngân sách ra sao | kn-08, kn-25 | lập kế hoạch theo tháng thấp, chi cố định trước, phần dư chia lại, đệm lớn hơn | giáo dục tài chính cho người làm tự do |
| Học phí và chi tiêu theo mùa (Tết...) | kn-09, kn-32 | chia nhỏ khoản lớn theo số tháng còn lại, lập quỹ riêng, chuẩn bị trước | giáo dục tài chính; thông báo học phí của trường (nếu nói số) |
| Lạm phát ảnh hưởng thế nào đến mục tiêu dài hạn | kn-11 | khái niệm, sức mua giảm, mục tiêu dài hạn nên tính trượt giá | tài liệu của Tổng cục Thống kê giải thích chỉ số giá tiêu dùng |
| Trả nợ tuyết lăn và thác tuyết (lãi cao trước) | kn-13 | hai thứ tự trả, ưu điểm mỗi cách, chọn cách duy trì được (FinMate đã có phần mô tả ngắn ở nhóm A) | giáo dục tài chính cá nhân về trả nợ |
| Nên trả nợ trước hay tiết kiệm trước | kn-14 | so lãi vay với lợi ích tiết kiệm, vẫn giữ quỹ khẩn cấp nhỏ, ưu tiên nợ lãi cao | như trên |
| Chi tiêu cố định và chi tiêu biến đổi | kn-15 | định nghĩa, ví dụ (thuê nhà, ăn uống...), vì sao tách riêng | giáo dục tài chính |
| Lãi kép là gì và vì sao quan trọng | kn-23 | định nghĩa, ví dụ bằng phần trăm và số năm (không dùng số tiền), thời gian là yếu tố chính | giáo dục tài chính |
| Chi phí cơ hội trong chi tiêu cá nhân | kn-24 | định nghĩa, ví dụ đời thường | giáo dục kinh tế/tài chính |
| Theo dõi chi tiêu nhỏ hằng ngày | kn-26 | khoản nhỏ cộng lại, thói quen ghi chép | giáo dục tài chính |
| Mục tiêu ngắn hạn và dài hạn | kn-27 | khoảng thời gian, cách đặt, công cụ tiết kiệm khác nhau | giáo dục tài chính |
| Nên để dành bao nhiêu phần trăm thu nhập | kn-19 | các tỷ lệ phổ biến, tùy hoàn cảnh, bắt đầu nhỏ | giáo dục tài chính; liên kết sang các phương pháp của FinMate |
| Làm sao biết mình chi tiêu quá tay | kn-35 | dấu hiệu (vượt hạn mức, rút tiết kiệm, vay để chi), cách kiểm tra theo tỷ lệ chi/thu | giáo dục tài chính |
| Khi nào nên cập nhật lại ngân sách | kn-30 | khi thu nhập hoặc hoàn cảnh đổi, khi hạn mức liên tục thiếu thực tế | giáo dục tài chính; ý này cũng có trong phần phương pháp của nhóm A |
| Lãi vay tiêu dùng và vì sao thường cao | kn-37 | vì sao cao (rủi ro, không tài sản đảm bảo), cách so sánh lãi suất; **số cụ thể thay đổi theo thời gian: nếu viết khoảng lãi thì ghi rõ ngày, hoặc bỏ số** | trang của Ngân hàng Nhà nước và quy định về lãi suất cho vay |
| Tỷ lệ nợ trên thu nhập an toàn | kn-38 | khái niệm, ngưỡng thường dùng theo khuyến nghị nào, vì sao | tài liệu của tổ chức giáo dục tài chính |
| Thuế thu nhập cá nhân với tiền lương | kn-18 | khái niệm giảm trừ, biểu thuế lũy tiến, kỳ khai; **số hiệu và ngày hiệu lực của văn bản pháp luật**; mức cụ thể có thể ở dạng phần trăm, tránh số tiền | cơ sở dữ liệu văn bản pháp luật, trang của Tổng cục Thuế |
| Bảo hiểm y tế ảnh hưởng thế nào đến kế hoạch tài chính | kn-17 | vai trò giảm chi phí khám chữa bệnh, tỷ lệ đóng và mức hưởng theo quy định (phần trăm), vì sao nên tính vào ngân sách | trang của Bảo hiểm xã hội Việt Nam, văn bản pháp luật |
| Ngân sách cuốn chiếu (rolling budget) | kn-21 | **FinMate không có phương pháp này.** Chọn một: viết giải thích khái niệm chung, hoặc đổi/bỏ câu `kn-21` khỏi bộ đánh giá | giáo dục tài chính |
| Chu kỳ ngân sách theo tháng và theo tuần | kn-40 | ưu nhược điểm hai chu kỳ; FinMate đặt hạn mức theo tháng và tổng kết theo tuần (đã có ở nhóm A) | giáo dục tài chính |

## 5. Nhóm C: giả định kinh tế (trang *Giả định kinh tế*, không phải tài liệu RAG)

Mỗi giả định gồm `key`, `rateBps` (phần trăm nhân 100: 3,5% thì điền 350), `unit` = `pct_per_year`, `sourceName`, `sourceUrl` (trang cụ thể) và `asOf`. Đổi giá trị = thêm bản mới, duyệt, bản cũ tự gỡ. Chi tiết nguồn đề xuất: `docs/agent-content-checklist.md` mục 1.

| Khóa | Ý nghĩa | Câu hỏi bộ đánh giá | Bắt buộc |
|---|---|---|---|
| `general_price_growth` | lạm phát bình quân hằng năm; mặc định của dự báo mục tiêu | kn-11 | có |
| `house_price_growth` | tăng giá nhà ở | kn-01 | nên có |
| `car_price_growth` | tăng giá ô tô | kn-22 | nên có |
| `wage_growth` | tăng lương bình quân | kn-36 | tùy chọn |
| `savings_deposit_rate` | lãi suất tiền gửi 12 tháng | kn-12 | tùy chọn |
| `tuition_growth` | tăng học phí | kn-09 | tùy chọn |

Nếu chưa có giả định nào, dự báo mục tiêu giữ giá không đổi theo thời gian và câu trả lời nói rõ điều đó. Điền `general_price_growth` trước vì nó là mặc định của mọi dự báo.
