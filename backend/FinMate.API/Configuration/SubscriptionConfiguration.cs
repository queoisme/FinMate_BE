using FinMate.Application.Subscriptions;

namespace FinMate.API.Configuration;

/// <summary>Đọc cấu hình thanh toán. Thiếu thì chỉ các API thanh toán trả 422; sai định dạng thì không cho khởi động, vì lúc đó tiền có thể đi sai chỗ mà không ai hay.</summary>
public static class SubscriptionConfiguration
{
    public const string CorsPolicyName = "SubscriptionWeb";
    private const string BillingWebPath = "/api/v1/billing/web";

    public static bool IsBillingWebPath(PathString path) => path.StartsWithSegments(BillingWebPath, StringComparison.OrdinalIgnoreCase);

    public static SubscriptionOptions Read(IConfiguration configuration)
        => new(
            configuration["SEPAY_MERCHANT_ID"],
            Url(configuration, "SEPAY_CHECKOUT_URL"),
            Url(configuration, "SEPAY_API_BASE_URL"),
            Url(configuration, "SUBSCRIPTION_WEB_URL"));

    private static string? Url(IConfiguration configuration, string name)
    {
        var value = configuration[name];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var valid = Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
        return valid ? value : throw new InvalidOperationException($"{name} phải là URL https đầy đủ.");
    }
}
