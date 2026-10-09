namespace FinMate.Application.Transactions;

/// <summary>
/// Độ tin cậy của một lần quét hóa đơn. Luôn dưới 0,85.
///
/// Docx phương thức 3 yêu cầu người dùng rà soát trước khi lưu, mà 0,85 lại là mốc xác nhận một chạm của Flow 1;
/// trả về một con số nằm trong vùng đó sẽ mâu thuẫn với chính yêu cầu kia. Cùng công thức với AI Service
/// (<c>receipt_ocr._confidence</c>) để kết quả không đổi khi việc đọc hóa đơn chuyển từ AI Service sang backend điều phối.
/// </summary>
public static class ReceiptConfidence
{
    public const double Ceiling = 0.80;
    public const double ReviewCeiling = 0.50;

    public static double Compute(bool hasAmount, bool amountFromLabel, bool hasMerchant, bool hasDate, bool needsReview)
    {
        if (!hasAmount)
        {
            return 0.0;
        }

        var score = amountFromLabel ? 0.55 : 0.35;
        score += hasMerchant ? 0.15 : 0.0;
        score += hasDate ? 0.10 : 0.0;
        var confidence = Math.Round(Math.Min(score, Ceiling), 2);

        // OCR service tự đánh dấu cần xem lại thì không được tỏ ra chắc chắn.
        return needsReview ? Math.Min(confidence, ReviewCeiling) : confidence;
    }
}
