# Ghi chú về từ điển câu chat/giọng nói

Mỗi tệp `*.json` là một nhóm (`kind`, `label`, `keywords`); tên tệp là tên nhóm. Đây là bản MẶC ĐỊNH nằm trong image. Bản chạy thật là tài liệu `chat_dictionary` trong
bảng `config_documents`: admin tải về, sửa, tải lên thành bản nháp, xem trước tác động, kích hoạt (hoàn tác = kích hoạt bản cũ). Không có bản trong DB thì dùng các tệp này.

Quy tắc khi sửa (bản nháp vi phạm bị từ chối): từ khóa viết thường, bỏ dấu, gọn khoảng trắng, dài 2-60 ký tự (câu chat được bỏ dấu trước khi so khớp nên từ có dấu không bao giờ khớp);
một từ chỉ thuộc một nhóm; không xóa nhóm có sẵn và không thêm nhóm mới (nhóm mới cần sửa code và lược đồ danh mục); `kind` không đổi.

- nhóm `salary`: thu nhập. Loại chung `other_income` không có từ khóa: chỉ chọn bằng nút/tên loại, không đoán từ các từ chung như "thu nhập", "nhận được".
- `education` và `family` là slug backend dùng thẳng, không nằm trong 9 nhóm hóa đơn.
- Trường tùy chọn `weak_keywords` (cùng luật với `keywords`, một từ chỉ nằm ở MỘT danh sách của MỘT nhóm): từ khóa **yếu** vẫn là từ khóa của nhóm với mọi nơi khác, nhưng bộ phân loại chỉ cho nó quyết khi câu không có từ khóa thường nào khớp. Dùng cho cụm chỉ nói tiền đến tay ai mà không nói mua gì ("cho con", "cho me"): "cho con 10k mua keo" ra Ăn uống, "cho con 500k" vẫn ra Gia đình. Thêm người nhận mới (vd. "cho co") chỉ cần thêm vào `weak_keywords` của `family` qua API admin, không sửa code.
- Trước đây có thêm hai bản chép (`ocr-service/data/chat_keywords.json` và các dòng `kind = 'chat'` trong `bill_keywords`) không ai đọc và đã lệch khỏi bản này; đã gỡ ở migration 0016.
