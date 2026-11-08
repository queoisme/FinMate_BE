using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Subscriptions;

public sealed record PublicPlanFeatureDto(string Key, string Name, string Description, bool Enabled, int? MonthlyLimit);

public sealed record PublicPlanDto(Guid Id, string Code, string Name, string? Description, long? MonthlyPriceCents, long? YearlyPriceCents, bool IsFree, IReadOnlyList<PublicPlanFeatureDto> Features);

public sealed record WebSessionLinkDto(string Url, DateTimeOffset ExpiresAt);

public sealed record BillingSessionDto(string SessionToken, DateTimeOffset ExpiresAt);

public sealed record CheckoutFormDto(string ActionUrl, IReadOnlyDictionary<string, string> Fields, string InvoiceNumber, string PlanName, SubscriptionCycle Cycle, long AmountCents);

public sealed record SubscriptionOrderDto(string InvoiceNumber, string PlanCode, string PlanName, SubscriptionCycle Cycle, long AmountCents, SubscriptionOrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);

public interface ISubscriptionCheckoutService
{
    Task<IReadOnlyList<PublicPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionOrderDto>> ListMyOrdersAsync(Guid userId, CancellationToken ct = default);
    Task<WebSessionLinkDto> CreateWebSessionAsync(Guid userId, Guid? planId, CancellationToken ct = default);
    Task<BillingSessionDto> ExchangeCodeAsync(string code, CancellationToken ct = default);
    Task<CheckoutFormDto> CheckoutAsync(Guid userId, Guid planId, SubscriptionCycle cycle, CancellationToken ct = default);
    Task<SubscriptionOrderDto> GetOrderAsync(Guid userId, string invoiceNumber, CancellationToken ct = default);
}

/// <summary>Đường đi từ nút Đăng ký trong app tới form đã ký gửi sang SePay. Ở đây chưa có quyền lợi nào được cấp; đó là việc của <see cref="ISubscriptionSettlementService"/>.</summary>
public sealed class SubscriptionCheckoutService : ISubscriptionCheckoutService
{
    private const int MaxPendingOrders = 10;
    private const int MyOrdersLimit = 50;
    private const string InvoicePrefix = "FM";
    private const string Operation = "PURCHASE";
    private static readonly TimeSpan OrderLifetime = TimeSpan.FromHours(24);

    /// <summary>Để IPN có cơ hội về trước khi job đối soát hỏi SePay.</summary>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(10);

    private readonly ISubscriptionRepository _repository;
    private readonly IBillingWebSessionStore _sessions;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;
    private readonly ISecretVault _vault;
    private readonly IAuditLogService _audit;
    private readonly SubscriptionOptions _options;

    public SubscriptionCheckoutService(
        ISubscriptionRepository repository,
        IBillingWebSessionStore sessions,
        IUserRepository users,
        ISystemConfigProvider config,
        ISecretVault vault,
        IAuditLogService audit,
        SubscriptionOptions options)
    {
        _repository = repository;
        _sessions = sessions;
        _users = users;
        _config = config;
        _vault = vault;
        _audit = audit;
        _options = options;
    }

    public async Task<IReadOnlyList<PublicPlanDto>> ListPlansAsync(CancellationToken ct = default)
        => (await _repository.ListPlansAsync(activeOnly: true, ct)).Select(ToPublic).ToList();

    public async Task<IReadOnlyList<SubscriptionOrderDto>> ListMyOrdersAsync(Guid userId, CancellationToken ct = default)
        => (await _repository.ListUserOrdersAsync(userId, MyOrdersLimit, ct)).Select(ToDto).ToList();

    public async Task<WebSessionLinkDto> CreateWebSessionAsync(Guid userId, Guid? planId, CancellationToken ct = default)
    {
        await RequireEnabledAsync(ct);
        await RequireConfiguredAsync(ct);
        await RequireActiveUserAsync(userId, ct);

        var code = await _sessions.IssueHandoffAsync(userId, ct);
        var query = $"code={code}" + (planId is { } plan ? $"&plan={plan}" : string.Empty);
        return new WebSessionLinkDto($"{_options.WebUrl!.TrimEnd('/')}/subscribe?{query}", DateTimeOffset.UtcNow.Add(BillingWebSessionStore.HandoffTtl));
    }

    public async Task<BillingSessionDto> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        await RequireEnabledAsync(ct);
        var userId = await _sessions.ConsumeHandoffAsync(code, ct)
            ?? throw new BusinessRuleException(SubscriptionErrorCodes.InvalidWebCode, "Liên kết đã hết hạn hoặc đã được dùng. Hãy nhấn Đăng ký lại trong app.");
        await RequireActiveUserAsync(userId, ct);

        return new BillingSessionDto(await _sessions.IssueSessionAsync(userId, ct), DateTimeOffset.UtcNow.Add(BillingWebSessionStore.SessionTtl));
    }

    public async Task<CheckoutFormDto> CheckoutAsync(Guid userId, Guid planId, SubscriptionCycle cycle, CancellationToken ct = default)
    {
        await RequireEnabledAsync(ct);
        var secret = await RequireConfiguredAsync(ct);
        await RequireActiveUserAsync(userId, ct);

        var now = DateTimeOffset.UtcNow;
        if (await _repository.CountPendingOrdersAsync(userId, now, ct) >= MaxPendingOrders)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.TooManyPendingOrders, "Bạn có quá nhiều đơn đang chờ thanh toán. Hãy thử lại sau.");
        }

        var plan = await _repository.GetPlanAsync(planId, ct);
        var amount = cycle == SubscriptionCycle.Monthly ? plan?.MonthlyPriceCents : plan?.YearlyPriceCents;
        if (plan is null || !plan.IsActive || plan.IsDefaultFree || amount is null)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.PlanNotAvailable, "Gói này hiện không bán theo chu kỳ đã chọn.");
        }

        var order = NewOrder(userId, plan, cycle, amount.Value, now);
        _repository.AddOrder(order);
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.SubscriptionOrderCreated, userId, null, new { orderId = order.Id, plan.Code }, ct);

        var fields = BuildFields(order, secret);
        return new CheckoutFormDto(_options.CheckoutUrl!, fields, order.InvoiceNumber, order.PlanName, cycle, order.AmountCents);
    }

    public async Task<SubscriptionOrderDto> GetOrderAsync(Guid userId, string invoiceNumber, CancellationToken ct = default)
    {
        await RequireActiveUserAsync(userId, ct);
        var order = await _repository.GetUserOrderByInvoiceAsync(userId, invoiceNumber, ct)
            ?? throw new NotFoundException("Đơn đăng ký", invoiceNumber);
        return ToDto(order);
    }

    private static SubscriptionOrder NewOrder(Guid userId, SubscriptionPlan plan, SubscriptionCycle cycle, long amount, DateTimeOffset now)
    {
        // Ghi đủ mọi chức năng, kể cả chức năng gói không có. Thiếu dòng thì lúc đọc sẽ hiểu là chức năng ra đời sau lúc mua và lấy theo gói miễn phí.
        var features = plan.Features.ToDictionary(f => f.FeatureKey);
        var grants = SubscriptionFeatures.All
            .Select(d => features.TryGetValue(d.Key, out var f) ? new FeatureGrant(d.Key, f.Enabled, f.MonthlyLimit) : new FeatureGrant(d.Key, false, null))
            .ToList();
        return new SubscriptionOrder
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = $"{InvoicePrefix}{VietnamTime.DateOf(now):yyMMdd}{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}",
            UserId = userId,
            PlanId = plan.Id,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            FeaturesJson = JsonSerializer.Serialize(grants, SubscriptionEntitlementService.Json),
            Cycle = cycle,
            AmountCents = amount,
            Status = SubscriptionOrderStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(OrderLifetime),
            NextCheckAt = now.Add(FirstCheckDelay),
        };
    }

    /// <summary><c>amount_cents</c> thực chất là đồng, nên gửi nguyên, không chia 100. Không gửi <c>customer_id</c>: SePay không cần biết id người dùng của FinMate.</summary>
    private Dictionary<string, string> BuildFields(SubscriptionOrder order, string secret)
    {
        var result = $"{_options.WebUrl!.TrimEnd('/')}/subscribe/result?invoice={order.InvoiceNumber}&status=";
        var cycleText = order.Cycle == SubscriptionCycle.Monthly ? "1 thang" : "1 nam";
        var fields = new Dictionary<string, string>
        {
            ["merchant"] = _options.MerchantId!,
            ["currency"] = SePayOrderInfo.CurrencyVnd,
            ["order_amount"] = order.AmountCents.ToString(CultureInfo.InvariantCulture),
            ["operation"] = Operation,
            ["order_description"] = $"FinMate {order.PlanCode} {cycleText}",
            ["order_invoice_number"] = order.InvoiceNumber,
            ["success_url"] = result + "success",
            ["error_url"] = result + "error",
            ["cancel_url"] = result + "cancel",
        };
        fields[SePaySignature.FieldName] = SePaySignature.Sign(fields, secret);
        return fields;
    }

    private async Task RequireEnabledAsync(CancellationToken ct)
    {
        if (!(await _config.GetAsync(ct)).Document.Features.Subscriptions)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.Disabled, "Tính năng đăng ký gói hiện chưa được bật.");
        }
    }

    private async Task<string> RequireConfiguredAsync(CancellationToken ct)
    {
        var secret = await _vault.GetAsync(BackendSecrets.SepaySecretKey, ct);
        if (!_options.IsComplete || string.IsNullOrWhiteSpace(secret))
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.NotConfigured, "Cổng thanh toán chưa được cấu hình trên máy chủ.");
        }

        return secret;
    }

    private async Task RequireActiveUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.IsLocked || user.DeletedAt is not null)
        {
            throw new ForbiddenException("Tài khoản này không thể đăng ký gói.");
        }
    }

    private static PublicPlanDto ToPublic(SubscriptionPlan plan)
    {
        var dto = SubscriptionPlanDto.From(plan);
        return new PublicPlanDto(
            plan.Id, plan.Code, plan.Name, plan.Description, plan.MonthlyPriceCents, plan.YearlyPriceCents, plan.IsDefaultFree,
            dto.Features.Select(f => new PublicPlanFeatureDto(f.Key, f.Name, f.Description, f.Enabled, f.MonthlyLimit)).ToList());
    }

    private static SubscriptionOrderDto ToDto(SubscriptionOrder o)
        => new(o.InvoiceNumber, o.PlanCode, o.PlanName, o.Cycle, o.AmountCents, o.Status, o.CreatedAt, o.PaidAt);
}
