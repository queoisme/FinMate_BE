namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Xếp các lệnh "học" gửi AI Service ra NGOÀI request: nhớ danh mục cửa hàng, gửi phản hồi người dùng sửa danh mục. Đây là việc làm tốt hơn lần sau, không ai chờ kết quả, nên giao dịch vừa lưu xong là trả lời ngay
/// (không chờ ai-service, dù chậm hay sập). Lỗi xếp hàng hay lỗi khi chạy chỉ ghi log: một bài học bị bỏ lỡ vô hại, còn làm chậm hay hỏng việc lưu giao dịch thì không.
/// Handler nhận <c>IAiLearningQueue?</c>; null (test cũ) thì gọi thẳng AI Service như trước.
/// </summary>
public interface IAiLearningQueue
{
    void LearnMerchant(LearnMerchantRequest request);

    void Feedback(FeedbackRequest request);
}
