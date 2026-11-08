namespace FinMate.Application.Subscriptions;

/// <summary>Một chức năng có thể bán theo gói.</summary>
public sealed record SubscriptionFeatureDefinition(string Key, string Name, string Description);

/// <summary>
/// Danh mục chức năng nằm trong code, không ở DB: mỗi chức năng phải có chỗ chặn thật ở server, nên thêm một dòng ở đây luôn đi kèm gắn
/// <c>RequireSubscriptionFeature</c> vào endpoint. Manager chỉ chọn chức năng nào thuộc gói nào.
/// </summary>
public static class SubscriptionFeatures
{
    public const string AiChat = "ai_chat";
    public const string VoiceInput = "voice_input";
    public const string ReceiptScan = "receipt_scan";

    public static readonly IReadOnlyList<SubscriptionFeatureDefinition> All =
    [
        new(AiChat, "Nhập bằng chat AI", "Gõ một câu để ghi giao dịch hoặc hỏi số liệu."),
        new(VoiceInput, "Nhập bằng giọng nói", "Đọc giao dịch thay vì gõ."),
        new(ReceiptScan, "Quét hóa đơn", "Chụp hóa đơn để điền sẵn giao dịch."),
    ];

    public static bool IsKnown(string key) => All.Any(f => f.Key == key);
}

/// <summary>Quyền lợi của một chức năng, cũng là dạng được chụp vào đơn và kỳ đăng ký.</summary>
public sealed record FeatureGrant(string Key, bool Enabled, int? MonthlyLimit);

public enum PaymentOutcome
{
    Granted,

    /// <summary>Đơn đã được xử lý từ trước. Không cấp thêm.</summary>
    AlreadySettled,

    /// <summary>Tiền về nhưng người dùng đã bị xóa cứng.</summary>
    UserMissing,
    OrderMissing,
}

/// <summary>Trạng thái đơn theo SePay. <paramref name="Amount"/> null khi SePay trả giá trị không đọc được, và khi đó đơn không được coi là khớp.</summary>
public sealed record SePayOrderInfo(string? Status, decimal? Amount, string? Currency, string? TransactionId)
{
    public const string StatusCaptured = "CAPTURED";
    public const string StatusCancelled = "CANCELLED";
    public const string CurrencyVnd = "VND";
}

/// <summary>Cấu hình không bí mật. Secret nằm ở <c>ISecretVault</c>.</summary>
public sealed record SubscriptionOptions(string? MerchantId, string? CheckoutUrl, string? ApiBaseUrl, string? WebUrl)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(MerchantId) && !string.IsNullOrWhiteSpace(CheckoutUrl)
        && !string.IsNullOrWhiteSpace(ApiBaseUrl) && !string.IsNullOrWhiteSpace(WebUrl);
}
