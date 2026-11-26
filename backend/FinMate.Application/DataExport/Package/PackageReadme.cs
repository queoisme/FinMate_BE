using System.Text;

namespace FinMate.Application.DataExport.Package;

/// <summary>README.txt của gói (BR-EXP-07): các file là gì, đơn vị tiền, múi giờ, ngày xuất, phiên bản định dạng, cái gì không có và vì sao, cách mở CSV (S1).</summary>
public static class PackageReadme
{
    /// <summary>Mô tả từng file. File nào không có trong gói thì không liệt kê.</summary>
    public static readonly IReadOnlyDictionary<string, string> FileDescriptions = new Dictionary<string, string>
    {
        ["profile.json"] = "Hồ sơ: email, tên hiển thị, mã FinMate, vai trò, thu nhập dự kiến, ngưỡng cảnh báo, giờ yên lặng, cài đặt cộng đồng.",
        ["notification_settings.json"] = "Cài đặt thông báo đẩy và cảnh báo.",
        ["consents.json"] = "Các đồng ý (Điều khoản, AI, dữ liệu huấn luyện, khảo sát, cộng đồng): phiên bản và thời điểm hiện tại, kèm lịch sử thay đổi từ ngày 25/11/2026.",
        ["devices.json"] = "Thiết bị đã đăng ký nhận thông báo (không có mã thiết bị) và trạng thái dịch vụ đọc thông báo ngân hàng trên điện thoại.",
        ["account_requests.json"] = "Các yêu cầu xoá tài khoản và các lần tải dữ liệu trước đây.",
        ["security_log.json"] = "Nhật ký bảo mật: đăng nhập, đổi mật khẩu, xác minh email, đồng ý, xin xoá, tải dữ liệu (kèm địa chỉ IP).",
        ["notifications.json"] = "Hộp thư trong app và các cảnh báo dự báo đã gửi.",
        ["accounts.json"] = "Ví và tài khoản, kể cả ví đã xoá.",
        ["categories.json"] = "Danh mục riêng bạn tạo và danh mục bạn đã ẩn.",
        ["category_rules.json"] = "Luật phân loại bạn đã dạy: nơi chi → danh mục, người nhận → danh mục.",
        ["transactions.csv"] = "Toàn bộ giao dịch (đã xác nhận và chờ duyệt), cùng định dạng với nút \"Xuất giao dịch (CSV)\".",
        ["transactions_deleted.csv"] = "Giao dịch đã xoá hoặc đã gộp vào giao dịch khác (cột \"Đã xoá lúc\").",
        ["bank_notifications.json"] = "Thông báo ngân hàng đã nhận: ứng dụng gửi, thời điểm, kết quả xử lý. Không có nội dung thông báo.",
        ["receipts.json"] = "Hoá đơn đã quét: tổng tiền, nơi bán, ngày, các món. Không có ảnh.",
        ["anomalies.json"] = "Giao dịch bị đánh dấu bất thường và cách bạn xử lý.",
        ["qr_transfers.json"] = "Các lần quét mã QR chuyển khoản.",
        ["budgets.json"] = "Ngân sách và từng kỳ (hạn mức, đã chi).",
        ["goals.json"] = "Mục tiêu tiết kiệm của bạn và các khoản bạn đã góp (kể cả vào mục tiêu nhóm).",
        ["recurring_items.json"] = "Khoản định kỳ: hoá đơn và thu nhập đều đặn, kể cả trả góp (số kỳ, ngày kết thúc, đã tất toán).",
        ["recurring_item_payments.json"] = "Các kỳ đã trả của khoản định kỳ: tự khớp, bạn đánh dấu hoặc tất toán; kể cả kỳ đã gỡ.",
        ["loans.json"] = "Sổ vay: các khoản cho vay / đi vay và từng lần giải ngân, trả.",
        ["finance_methods.json"] = "Phương pháp tài chính đã chọn, các đề xuất và câu trả lời khảo sát.",
        ["insights.json"] = "Nhận xét chi tiêu FinMate đã tạo cho bạn.",
        ["forecasts.json"] = "Kết quả dự báo số dư và phản hồi bạn đã gửi.",
        ["groups.json"] = "Nhóm đang và đã tham gia, vai trò, cùng phần của riêng bạn: chia hoá đơn, thanh toán, góp quỹ, lời mời, đề xuất. Thành viên khác chỉ ghi vai trò.",
        ["support.json"] = "Ticket hỗ trợ của bạn và tin nhắn hai bên (không có ảnh, không có ghi chú nội bộ), các lần bạn cho phép nhân viên xem dữ liệu và nhật ký nhân viên đã xem.",
        ["community.json"] = "Bài viết, bình luận, lượt thích và báo cáo của bạn trong Cộng đồng (không có ảnh).",
        ["subscriptions.json"] = "Đơn mua gói, các kỳ sử dụng và số lượt đã dùng.",
        ["agent_memory.json"] = "Bộ nhớ dài hạn của Trợ lý (các sở thích bạn đã cho Trợ lý nhớ).",
    };

    public static string Build(DateTimeOffset exportedAt, IEnumerable<string> files, bool agentMemoryUnavailable)
    {
        var text = new StringBuilder();
        text.AppendLine("DỮ LIỆU CÁ NHÂN CỦA BẠN TRÊN FINMATE");
        text.AppendLine("====================================");
        text.AppendLine();
        text.AppendLine($"Ngày xuất: {TransactionCsvFormat.Iso(exportedAt)}");
        text.AppendLine($"Phiên bản định dạng: {TransactionCsvFormat.FormatVersion}");
        text.AppendLine();
        text.AppendLine("QUY ƯỚC");
        text.AppendLine("- Tiền: số nguyên, đơn vị đồng (VND). Trường JSON có đuôi \"Cents\" (vd. amountCents) cũng là đồng, giữ tên theo API của FinMate.");
        text.AppendLine("- Thời gian: giờ Việt Nam (UTC+07:00), dạng ISO 8601 (vd. 2026-11-25T08:30:00+07:00). Ngày dạng yyyy-MM-dd.");
        text.AppendLine("- File JSON: { \"formatVersion\", \"exportedAt\", \"items\": [...] } cho danh sách, hoặc \"data\": {...} cho một đối tượng. Tên trường tiếng Anh, như API.");
        text.AppendLine("- File CSV: UTF-8 có BOM, phân cách bằng dấu phẩy, xuống dòng CRLF. Ô bắt đầu bằng = + - @ được thêm dấu ' phía trước để bảng tính không chạy như công thức.");
        text.AppendLine("- Cột \"Nguồn\" ghi \"Tay\" cho cả giao dịch nhập tay và giao dịch tạo qua chat.");
        text.AppendLine("- Cột \"Số tiền tính vào chi tiêu\": khoản chi được tính là số dương, tiền hoàn là số âm, còn lại 0. Cộng cột này trong một tháng ra tổng chi của báo cáo tháng đó,");
        text.AppendLine("  trừ khi tháng đó tiền hoàn nhiều hơn tiền chi (báo cáo khi đó ghi chi 0).");
        text.AppendLine();
        text.AppendLine("CÁCH MỞ FILE CSV");
        text.AppendLine("- Excel: Data (Dữ liệu) → From Text/CSV (Từ văn bản/CSV) → chọn file → File Origin chọn \"65001: Unicode (UTF-8)\", Delimiter chọn \"Comma\" → Load.");
        text.AppendLine("  Mở bằng cách bấm đúp có thể dồn mọi cột vào một ô nếu máy đặt vùng Việt Nam (Excel khi đó tách cột bằng dấu chấm phẩy).");
        text.AppendLine("  Nút \"Xuất giao dịch (CSV)\" trong app có tuỳ chọn dấu chấm phẩy cho trường hợp này.");
        text.AppendLine("- Google Sheets: File → Import (Nhập) → Upload → chọn file → Separator type: \"Comma\" → Import data.");
        text.AppendLine();
        text.AppendLine("CÁC FILE TRONG GÓI");
        foreach (var file in files)
        {
            text.AppendLine($"- {file}: {FileDescriptions.GetValueOrDefault(file, string.Empty)}");
        }

        if (agentMemoryUnavailable)
        {
            text.AppendLine();
            text.AppendLine("Lưu ý: lúc tạo gói, dịch vụ Trợ lý không trả lời nên agent_memory.json chưa có nội dung. Bạn xem được bộ nhớ này trong app (Trợ lý → Bộ nhớ),");
            text.AppendLine("hoặc gửi yêu cầu tải dữ liệu lại sau.");
        }

        text.AppendLine();
        text.AppendLine("KHÔNG CÓ TRONG GÓI, VÀ VÌ SAO");
        text.AppendLine("- Mật khẩu, phiên đăng nhập, mã thiết bị nhận thông báo, mã OTP: là thông tin bảo mật.");
        text.AppendLine("- Nội dung thô của thông báo ngân hàng, số dư sau giao dịch, mã tham chiếu ngân hàng: là dữ liệu kỹ thuật dùng để đọc thông báo.");
        text.AppendLine("  Nội dung thông báo tự xoá sau 90 ngày.");
        text.AppendLine("- Ảnh (hoá đơn, ticket hỗ trợ, cộng đồng): ảnh hoá đơn không bao giờ được lưu; ảnh ticket và cộng đồng không đưa vào gói.");
        text.AppendLine("- Dữ liệu của người khác: giao dịch thành viên khác chia sẻ vào nhóm, tên và email thành viên khác, ghi chú nội bộ của nhân viên hỗ trợ.");
        text.AppendLine("- Dữ liệu ở máy chủ AI (độ tin cậy, phiên bản mô hình, mẫu huấn luyện): chỉ gắn với mã băm một chiều của tài khoản, không truy ngược được về bạn.");
        text.AppendLine("  Mẫu huấn luyện bị xoá khi bạn xoá tài khoản.");
        text.AppendLine("- Bản sao tạm khi gộp giao dịch và bảng tổng hợp theo ngày: là bản sao kỹ thuật của dữ liệu đã có trong transactions.csv.");
        text.AppendLine();
        text.AppendLine("Gói này được giữ trên máy chủ FinMate trong thời gian ngắn rồi tự xoá. Hãy cất file ở nơi an toàn: nó chứa toàn bộ dữ liệu tài chính của bạn.");
        return text.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }
}
