namespace FinMate.Application.Common.Exceptions;

public static class SubscriptionErrorCodes
{
    public const string Disabled = "SUBSCRIPTION_DISABLED";
    public const string NotConfigured = "SUBSCRIPTION_NOT_CONFIGURED";
    public const string FeatureLocked = "SUBSCRIPTION_FEATURE_LOCKED";
    public const string LimitReached = "SUBSCRIPTION_LIMIT_REACHED";
    public const string PlanNotAvailable = "SUBSCRIPTION_PLAN_NOT_AVAILABLE";
    public const string PlanCodeTaken = "SUBSCRIPTION_PLAN_CODE_TAKEN";
    public const string FreePlanProtected = "SUBSCRIPTION_FREE_PLAN_PROTECTED";
    public const string PriceRequired = "SUBSCRIPTION_PRICE_REQUIRED";
    public const string PlanHasNoFeatures = "SUBSCRIPTION_PLAN_HAS_NO_FEATURES";
    public const string UnknownFeature = "SUBSCRIPTION_UNKNOWN_FEATURE";
    public const string InvalidWebCode = "SUBSCRIPTION_INVALID_WEB_CODE";
    public const string TooManyPendingOrders = "SUBSCRIPTION_TOO_MANY_PENDING_ORDERS";
    public const string ProviderUnavailable = "SUBSCRIPTION_PROVIDER_UNAVAILABLE";
}

/// <summary>Không tra được SePay. Trả 503 để SePay coi như chưa nhận; job đối soát sẽ xử lý tiếp. Không mang chi tiết phản hồi của nhà cung cấp.</summary>
public sealed class SePayUnavailableException : FinMateException
{
    public override string ErrorCode => SubscriptionErrorCodes.ProviderUnavailable;
    public override int HttpStatusCode => 503;

    public SePayUnavailableException()
        : base("Chưa kết nối được cổng thanh toán. Vui lòng thử lại sau.")
    {
    }
}
