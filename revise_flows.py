from docx import Document
from docx.oxml import OxmlElement
from docx.text.paragraph import Paragraph

SOURCE = r'E:\Ki 9\Capstone_Project\Doc\FinMate_User_Insight_And_4_Core_Flows_Specification_Report_Updated.docx'
OUTPUT = r'E:\Ki 9\Capstone_Project\Doc\FinMate_User_Insight_And_6_Core_Flows_Specification_Report.docx'

doc = Document(SOURCE)

def set_text(paragraph, value):
    for run in paragraph.runs:
        run.text = ''
    paragraph.add_run(value)

set_text(doc.paragraphs[2], 'BÁO CÁO PHÂN TÍCH INSIGHT NGƯỜI DÙNG\nVÀ ĐẶC TẢ 6 CORE FLOWS FINMATE')
set_text(doc.paragraphs[36], 'PHẦN 3: ĐẶC TẢ 6 CORE FLOWS FINMATE')
set_text(doc.paragraphs[37], 'Sáu luồng chính gồm bốn cách tiếp nhận dữ liệu có xử lý đầu vào khác nhau — đọc thông báo, chatbot, giọng nói và OCR — cùng hai luồng sử dụng dữ liệu đã ghi nhận: quản lý tài chính và dự báo tài chính. Các luồng nhập liệu cùng đi tới bước kiểm tra, xác nhận giao dịch; mỗi luồng giữ cơ chế trích xuất và xử lý lỗi riêng.')

sections = [
('Heading 2', '3.1 CORE FLOW 1: ĐỌC THÔNG BÁO GIAO DỊCH'),
('Normal', 'Mã luồng: CF-01 | Tác nhân: Android OS, người dùng, dịch vụ AI | Kích hoạt: ứng dụng ngân hàng hoặc ví điện tử phát thông báo biến động số dư.'),
('Normal', '1. Người dùng cấp quyền đọc thông báo và chọn nguồn được phép theo dõi. Ứng dụng nhận thông báo trên thiết bị, kiểm tra nguồn hợp lệ và lọc thông tin nhạy cảm trước khi gửi dữ liệu cần phân tích.'),
('Normal', '2. Hệ thống nhận diện thông báo giao dịch, trích xuất số tiền, chiều thu/chi, thời điểm, tài khoản và nội dung giao dịch; loại thông báo quảng cáo hoặc thông báo không đủ dữ liệu.'),
('Normal', '3. Hệ thống đối chiếu giao dịch đã có để tránh ghi trùng khi ngân hàng, ví và sàn thương mại điện tử cùng báo một lần thanh toán. Sau đó đề xuất danh mục và tạo giao dịch chờ xác nhận nếu cần.'),
('Normal', '4. Người dùng kiểm tra và xác nhận hoặc sửa thông tin. Giao dịch đã xác nhận mới cập nhật sổ thu chi, số dư và ngân sách liên quan. Khi thiếu dữ liệu hoặc độ tin cậy thấp, hệ thống giữ bản nháp để người dùng bổ sung; không tự đoán số tiền.'),
('Heading 2', '3.2 CORE FLOW 2: CHATBOT NHẬP GIAO DỊCH'),
('Normal', 'Mã luồng: CF-02 | Tác nhân: người dùng, chatbot, dịch vụ AI | Kích hoạt: người dùng gửi câu tiếng Việt tự nhiên, ví dụ “ăn trưa 45 nghìn bằng tiền mặt”.'),
('Normal', '1. Chatbot nhận văn bản và xác định ý định: ghi thu, ghi chi, chuyển tiền, hỏi thông tin tài chính hoặc yêu cầu làm rõ. Câu hỏi tra cứu không được tự biến thành giao dịch.'),
('Normal', '2. Với ý định ghi giao dịch, hệ thống trích xuất số tiền, loại giao dịch, danh mục, thời gian, tài khoản và mô tả. Dữ liệu chưa rõ được hỏi lại ngay trong hội thoại; không tự chọn số tiền hoặc tài khoản khi câu nói mơ hồ.'),
('Normal', '3. Chatbot trả bản xem trước để người dùng xác nhận hoặc sửa. Sau xác nhận, backend ghi giao dịch một lần, cập nhật các tổng hợp liên quan và trả kết quả. Nếu người dùng hủy, không ghi giao dịch.'),
('Heading 2', '3.3 CORE FLOW 3: NHẬP GIAO DỊCH BẰNG GIỌNG NÓI'),
('Normal', 'Mã luồng: CF-03 | Tác nhân: người dùng, microphone, bộ chuyển giọng nói thành văn bản | Kích hoạt: người dùng nhấn nút ghi âm hoặc lối tắt nhập nhanh.'),
('Normal', '1. Ứng dụng thu âm sau khi được cấp quyền microphone, chuyển lời nói tiếng Việt thành văn bản và cho người dùng xem bản chép để sửa lỗi nhận dạng, nhất là số tiền, đơn vị tiền và tên nơi mua.'),
('Normal', '2. Văn bản đã được người dùng kiểm tra đi qua bước hiểu giao dịch tự nhiên như chatbot. Hệ thống tách số tiền, chiều thu/chi, thời gian, danh mục và tài khoản, rồi hiển thị bản xem trước.'),
('Normal', '3. Người dùng xác nhận mới ghi giao dịch. Nếu âm thanh ồn, im lặng, không nhận diện được hoặc số tiền mơ hồ, ứng dụng yêu cầu nói lại hay chuyển sang nhập văn bản; không ghi dữ liệu từ bản chép không chắc chắn.'),
('Heading 2', '3.4 CORE FLOW 4: OCR HÓA ĐƠN VÀ ẢNH GIAO DỊCH'),
('Normal', 'Mã luồng: CF-04 | Tác nhân: người dùng, camera/thư viện ảnh, OCR, dịch vụ phân loại | Kích hoạt: người dùng chụp hoặc tải ảnh hóa đơn, biên lai hay ảnh đơn hàng.'),
('Normal', '1. Ứng dụng nhận ảnh và kiểm tra khả năng đọc. OCR trích xuất cửa hàng, ngày giờ, tổng tiền và các dòng mặt hàng nếu có; thông tin cá nhân trong ảnh được giới hạn trước các bước phân tích tiếp theo.'),
('Normal', '2. Hệ thống đối chiếu tổng tiền với các dòng hàng, đề xuất danh mục cho từng món hoặc toàn hóa đơn, và kiểm tra khả năng trùng với giao dịch đã ghi từ thông báo. Mục không đọc chắc được đánh dấu cần sửa.'),
('Normal', '3. Người dùng xem ảnh cùng dữ liệu nhận dạng, chỉnh các trường sai và chọn cách ghi phù hợp trước khi xác nhận. Ảnh mờ, thiếu tổng tiền hoặc tổng dòng hàng không khớp phải được yêu cầu kiểm tra; không tự ghi số tiền suy đoán.'),
('Heading 2', '3.5 CORE FLOW 5: QUẢN LÝ TÀI CHÍNH'),
('Normal', 'Mã luồng: CF-05 | Tác nhân: người dùng, backend tài chính | Kích hoạt: người dùng mở sổ giao dịch, tài khoản, ngân sách, mục tiêu hoặc có giao dịch được xác nhận.'),
('Normal', '1. Người dùng xem và chỉnh sửa giao dịch, phân loại thu/chi/chuyển giữa tài khoản, quản lý tài khoản và danh mục. Mọi thay đổi cần phản ánh nhất quán trong các số tổng hợp; chuyển tiền nội bộ không được tính thành thu nhập hoặc chi tiêu.'),
('Normal', '2. Người dùng tạo và theo dõi ngân sách theo danh mục, mục tiêu tiết kiệm và báo cáo dòng tiền. Hệ thống trừ dần chi tiêu hợp lệ, hiển thị phần còn lại, so sánh kỳ trước và cảnh báo khi ngân sách đạt các ngưỡng đã quy định.'),
('Normal', '3. Khi sửa, hủy hoặc xác nhận giao dịch, hệ thống tính lại phần tổng hợp bị ảnh hưởng và hiển thị trạng thái mới. Dữ liệu chưa xác nhận không làm thay đổi ngân sách hay số dư chính thức.'),
('Heading 2', '3.6 CORE FLOW 6: DỰ BÁO TÀI CHÍNH'),
('Normal', 'Mã luồng: CF-06 | Tác nhân: người dùng, bộ máy dự báo | Kích hoạt: người dùng mở màn hình dự báo, thay đổi dữ liệu tài chính hoặc hệ thống chạy dự báo định kỳ.'),
('Normal', '1. Hệ thống dùng giao dịch đã xác nhận, số dư, thu nhập, ngân sách và nghĩa vụ định kỳ sẵn có để ước tính dòng tiền tương lai. Giao dịch chuyển tiền nội bộ không được cộng thành thu hoặc chi trong dự báo.'),
('Normal', '2. Kết quả gồm xu hướng chi tiêu, số dư dự phóng, mức chi gợi ý và cảnh báo nguy cơ thiếu tiền khi đủ dữ liệu. Người dùng có thể nhập kịch bản mua sắm hoặc trả góp để xem tác động giả định trước khi quyết định.'),
('Normal', '3. Màn hình nêu rõ kỳ dự báo, dữ liệu đầu vào và giả định. Khi thiếu thu nhập hoặc lịch nghĩa vụ, hệ thống chỉ hiện phần dự báo có cơ sở và yêu cầu bổ sung dữ liệu; kịch bản giả định không được ghi thành giao dịch thật.'),
]

anchor = doc.paragraphs[64]._p
for paragraph in doc.paragraphs[38:64]:
    paragraph._element.getparent().remove(paragraph._element)
for style, value in sections:
    element = OxmlElement('w:p')
    anchor.addprevious(element)
    new_paragraph = Paragraph(element, doc._body)
    new_paragraph.style = style
    new_paragraph.add_run(value)

set_text(doc.paragraphs[-3], 'PHẦN 4: MA TRẬN SO SÁNH TRƯỚC VÀ SAU CẢI TIẾN CHO 6 CORE FLOWS')
set_text(doc.paragraphs[-2], 'Bảng dưới đây đối chiếu phạm vi cũ với sáu luồng chính sau khi tách riêng từng cơ chế nhập liệu và hai chức năng sử dụng dữ liệu tài chính.')

matrix = doc.tables[6]
for row in list(matrix.rows)[1:]:
    matrix._tbl.remove(row._tr)
matrix_rows = [
('CF-01 Đọc thông báo', 'Đọc biến động từ một số app ngân hàng; dễ trùng khi nhiều nguồn cùng báo.', 'Lọc nguồn, trích xuất giao dịch, chống trùng đa nguồn, đề xuất danh mục và chờ người dùng xác nhận khi cần.'),
('CF-02 Chatbot', 'Chưa có luồng nhập hội thoại độc lập.', 'Hiểu ý định từ câu tiếng Việt, hỏi lại trường thiếu, cho xem trước và chỉ ghi sau xác nhận.'),
('CF-03 Voice to text', 'Gộp trong luồng nhập tiền mặt; lỗi nhận dạng tiếng Việt khó kiểm soát.', 'Chuyển âm thanh thành văn bản, cho sửa bản chép, phân tích giao dịch và xử lý riêng ca âm thanh không rõ.'),
('CF-04 OCR', 'Gộp trong luồng nhập tiền mặt; hóa đơn mờ và dòng hàng dễ sai.', 'Đọc ảnh, bóc tách tổng tiền và từng món, đối chiếu dữ liệu, đánh dấu trường không chắc và kiểm tra trùng.'),
('CF-05 Quản lý tài chính', 'Theo dõi thu chi, ngân sách và báo cáo cơ bản.', 'Quản lý giao dịch, tài khoản, danh mục, ngân sách, mục tiêu và các số tổng hợp nhất quán.'),
('CF-06 Dự báo tài chính', 'Chưa có luồng dự báo tài chính riêng.', 'Dự báo dòng tiền và số dư, cảnh báo thiếu tiền, gợi ý mức chi và mô phỏng kịch bản có nêu giả định.'),
]
for values in matrix_rows:
    cells = matrix.add_row().cells
    for cell, value in zip(cells, values):
        cell.text = value

doc.tables[0].cell(0,0).text = '[TÓM TẮT MỤC TIÊU TÀI LIỆU]\nBáo cáo phân tích insight người dùng và đặc tả 6 luồng chính của FinMate. Bốn luồng tiếp nhận dữ liệu được tách theo cơ chế xử lý riêng; quản lý và dự báo tài chính sử dụng dữ liệu đã ghi nhận.'
doc.tables[5].cell(3,0).text = 'Bổ sung luồng Dự báo tài chính'
doc.tables[5].cell(3,1).text = 'Người dùng cần biết nguy cơ thiếu tiền và tác động của quyết định chi tiêu trước khi thực hiện.'
doc.tables[5].cell(3,2).text = 'Dự báo tài chính trở thành CF-06 độc lập, sử dụng dữ liệu đã xác nhận để dự phóng dòng tiền và mô phỏng kịch bản.'
doc.save(OUTPUT)
print(OUTPUT)
