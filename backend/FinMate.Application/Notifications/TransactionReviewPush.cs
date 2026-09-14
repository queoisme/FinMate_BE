using System.Globalization;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Notifications;

/// <param name="Data">
/// Đi kèm push để client dựng được hành động. KHÔNG BAO GIỜ ghi log — mang số tiền.
/// </param>
public record TransactionReviewPush(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data);

/// <summary>
/// Chọn giữa hai cách hỏi người dùng về một giao dịch nháp, theo docx Flow 1:
///
/// - bước 5.2 (≥85%): thông báo nổi nhanh, một nút "Xác nhận";
/// - bước 5.3 (&lt;85%): hộp thoại cho chọn danh mục khác.
///
/// Thuần và tách khỏi handler để test được mà không cần AI Service lẫn FCM.
/// </summary>
public static class TransactionReviewPushBuilder
{
    /// <summary>
    /// Ngưỡng của docx bước 5.2/5.3. Docx TỰ MÂU THUẪN: bảng tình huống biên ghi 80% cho đúng
    /// hành vi này. Lấy 85% theo bản Step-by-Step (mô tả chi tiết nhất của chính nhánh này),
    /// và để ở một chỗ duy nhất để đổi là xong.
    /// </summary>
    public const double OneTapThreshold = 0.85;

    public const string ActionOneTap = "confirm_one_tap";
    public const string ActionChooseCategory = "choose_category";

    /// <summary>
    /// Mức tin cậy quyết định nhánh: THẤP NHẤT giữa bóc số và phân loại.
    ///
    /// Không lấy riêng điểm phân loại: một chạm xác nhận TOÀN BỘ bản ghi — số tiền, loại,
    /// danh mục — nên mắt xích yếu nhất phải quyết định. Bóc số 0,70 kèm phân loại 0,95 mà
    /// cho một chạm là chốt nhanh một con số tiền có thể sai.
    ///
    /// Không tính điểm của Classifier: nó trả lời "đây có phải giao dịch không", một cổng đã
    /// đi qua rồi mới có nháp — đưa vào là đếm hai lần cùng một thứ.
    ///
    /// Thiếu một trong hai → <c>null</c>, và <see cref="Build"/> hiểu là KHÔNG đủ tin. Dữ
    /// liệu vắng mặt không bao giờ được quy ra "chắc chắn".
    /// </summary>
    public static double? ReviewConfidence(double? extraction, double? categorization)
        => extraction is { } e && categorization is { } c ? Math.Min(e, c) : null;

    /// <summary>
    /// Ngăn nghìn bằng dấu CHẤM, kiểu Việt Nam.
    ///
    /// Dựng tay thay vì gọi <c>CultureInfo.GetCultureInfo("vi-VN")</c>: solution bật
    /// <c>InvariantGlobalization</c> nên culture đó không tồn tại lúc chạy và sẽ ném ngay khi
    /// khởi tạo lớp này. Mà <c>:N0</c> trần thì cho ra "75,000đ" — dấu phẩy kiểu Mỹ, trong
    /// khi người Việt viết "75.000đ". Dựng tay còn tất định hơn: không phụ thuộc ICU có mặt
    /// hay không, nên cùng một đoạn code cho ra cùng một chuỗi ở mọi nơi triển khai.
    /// </summary>
    private static readonly NumberFormatInfo VietnamMoney = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    public static TransactionReviewPush Build(
        Transaction transaction, string? categoryName, double? confidence)
    {
        var amount = transaction.AmountCents.ToString("N0", VietnamMoney) + "đ";
        var merchant = string.IsNullOrWhiteSpace(transaction.MerchantName)
            ? null
            : transaction.MerchantName.Trim();
        var verb = transaction.TransactionType == TransactionType.Credit ? "nhận" : "chi";

        var data = new Dictionary<string, string>
        {
            ["type"] = "transaction_review",
            ["transactionId"] = transaction.Id.ToString(),
            ["amountCents"] = transaction.AmountCents.ToString(),
            ["transactionType"] = transaction.TransactionType.ToString(),
        };

        if (merchant is not null)
        {
            data["merchantName"] = merchant;
        }

        if (transaction.Category?.Slug is { } slug)
        {
            data["categorySlug"] = slug;
        }

        var at = merchant is null ? "" : $" tại {merchant}";

        if (confidence >= OneTapThreshold)
        {
            data["action"] = ActionOneTap;

            // Câu của docx bước 5.2, giữ nguyên dạng câu hỏi: người dùng chỉ cần đọc lướt rồi
            // chạm một nút, nên toàn bộ thông tin phải nằm trong chính dòng thông báo.
            var scope = categoryName is null ? "" : $" ({categoryName})";
            return new TransactionReviewPush(
                "Xác nhận nhanh",
                $"Bạn vừa {verb} {amount}{at}{scope}?",
                data);
        }

        data["action"] = ActionChooseCategory;

        // Không nêu danh mục trong câu như nhánh trên: ở đây AI không chắc, mà đọc một danh
        // mục lên như thể đã chốt sẽ khiến người dùng gật theo thay vì chọn lại.
        return new TransactionReviewPush(
            "Giao dịch mới cần phân loại",
            $"Bạn vừa {verb} {amount}{at}. Chọn danh mục giúp Mascot nhé.",
            data);
    }
}
