using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Subscriptions;

public sealed record PlanFeatureDto(string Key, string Name, string Description, bool Enabled, int? MonthlyLimit);

public sealed record SubscriptionPlanDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    long? MonthlyPriceCents,
    long? YearlyPriceCents,
    bool IsDefaultFree,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<PlanFeatureDto> Features)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan)
    {
        var features = plan.Features.ToDictionary(f => f.FeatureKey);
        return new SubscriptionPlanDto(
            plan.Id, plan.Code, plan.Name, plan.Description, plan.MonthlyPriceCents, plan.YearlyPriceCents, plan.IsDefaultFree, plan.IsActive, plan.SortOrder,
            SubscriptionFeatures.All.Select(d =>
            {
                var feature = features.GetValueOrDefault(d.Key);
                return new PlanFeatureDto(d.Key, d.Name, d.Description, feature?.Enabled ?? false, feature?.MonthlyLimit);
            }).ToList());
    }
}

public sealed record CreatePlanInput(string Code, string Name, string? Description, long? MonthlyPriceCents, long? YearlyPriceCents, int SortOrder = 0);

/// <summary>Trường null là giữ nguyên. Bỏ bán theo một chu kỳ thì dùng <c>RemoveMonthlyPrice</c>, <c>RemoveYearlyPrice</c>, vì null không phân biệt được với "không sửa".</summary>
public sealed record UpdatePlanInput(
    string? Name,
    string? Description,
    long? MonthlyPriceCents,
    long? YearlyPriceCents,
    bool RemoveMonthlyPrice = false,
    bool RemoveYearlyPrice = false,
    bool? IsActive = null,
    int? SortOrder = null);

public sealed record PlanFeatureInput(string Key, bool Enabled, int? MonthlyLimit);

/// <summary>Cố ý không có id người dùng: dữ liệu của một khách chỉ xem được qua <c>IOpsCustomerDataService</c>. Mã đơn đủ để đối chiếu trên SePay và với khách khi họ mở ticket.</summary>
public sealed record OpsSubscriptionOrderDto(
    Guid Id,
    string InvoiceNumber,
    string PlanCode,
    SubscriptionCycle Cycle,
    long AmountCents,
    SubscriptionOrderStatus Status,
    string? ReviewReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record OpsSubscriptionOrderPageDto(IReadOnlyList<OpsSubscriptionOrderDto> Items, int Total);

public interface IOpsSubscriptionService
{
    Task<IReadOnlyList<SubscriptionFeatureDefinition>> ListFeaturesAsync(Guid actorId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(Guid actorId, CancellationToken ct = default);
    Task<SubscriptionPlanDto> CreatePlanAsync(Guid actorId, CreatePlanInput input, string? ip, CancellationToken ct = default);
    Task<SubscriptionPlanDto> UpdatePlanAsync(Guid actorId, Guid planId, UpdatePlanInput input, string? ip, CancellationToken ct = default);
    Task DeletePlanAsync(Guid actorId, Guid planId, string? ip, CancellationToken ct = default);
    Task<SubscriptionPlanDto> SetFeaturesAsync(Guid actorId, Guid planId, IReadOnlyList<PlanFeatureInput> features, string? ip, CancellationToken ct = default);
    Task<OpsSubscriptionOrderPageDto> ListOrdersAsync(Guid actorId, SubscriptionOrderStatus? status, int offset, int limit, CancellationToken ct = default);
}

public sealed class CreatePlanInputValidator : AbstractValidator<CreatePlanInput>
{
    public CreatePlanInputValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[a-z0-9_]{2,40}$").WithMessage("Mã gói chỉ gồm chữ thường không dấu, số và dấu gạch dưới, từ 2 đến 40 ký tự.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.MonthlyPriceCents).InclusiveBetween(1, OpsSubscriptionService.MaxPriceCents).When(x => x.MonthlyPriceCents is not null);
        RuleFor(x => x.YearlyPriceCents).InclusiveBetween(1, OpsSubscriptionService.MaxPriceCents).When(x => x.YearlyPriceCents is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdatePlanInputValidator : AbstractValidator<UpdatePlanInput>
{
    public UpdatePlanInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.MonthlyPriceCents).InclusiveBetween(1, OpsSubscriptionService.MaxPriceCents).When(x => x.MonthlyPriceCents is not null);
        RuleFor(x => x.YearlyPriceCents).InclusiveBetween(1, OpsSubscriptionService.MaxPriceCents).When(x => x.YearlyPriceCents is not null);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).When(x => x.SortOrder is not null);
    }
}

/// <summary>Cấu hình gói của OpsManager và Admin. Sửa gói không chạm tới đơn và kỳ đã bán, vì chúng giữ bản chụp riêng.</summary>
public sealed class OpsSubscriptionService : IOpsSubscriptionService
{
    public const long MaxPriceCents = 1_000_000_000;
    private const int MaxPageSize = 100;

    private readonly IOpsAccessService _access;
    private readonly ISubscriptionRepository _repository;
    private readonly IAuditLogService _audit;
    private readonly IValidator<CreatePlanInput> _createValidator;
    private readonly IValidator<UpdatePlanInput> _updateValidator;

    public OpsSubscriptionService(
        IOpsAccessService access,
        ISubscriptionRepository repository,
        IAuditLogService audit,
        IValidator<CreatePlanInput> createValidator,
        IValidator<UpdatePlanInput> updateValidator)
    {
        _access = access;
        _repository = repository;
        _audit = audit;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<SubscriptionFeatureDefinition>> ListFeaturesAsync(Guid actorId, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return SubscriptionFeatures.All;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(Guid actorId, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return (await _repository.ListPlansAsync(activeOnly: false, ct)).Select(SubscriptionPlanDto.From).ToList();
    }

    public async Task<SubscriptionPlanDto> CreatePlanAsync(Guid actorId, CreatePlanInput input, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        await _createValidator.ValidateAndThrowAsync(input, ct);
        RequirePrice(input.MonthlyPriceCents, input.YearlyPriceCents);
        if (await _repository.PlanCodeExistsAsync(input.Code, ct))
        {
            throw new ConflictException(SubscriptionErrorCodes.PlanCodeTaken, "Mã gói này đã được dùng, kể cả ở gói đã xóa.");
        }

        var now = DateTimeOffset.UtcNow;
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Code = input.Code,
            Name = input.Name.Trim(),
            Description = input.Description?.Trim(),
            MonthlyPriceCents = input.MonthlyPriceCents,
            YearlyPriceCents = input.YearlyPriceCents,
            // Chưa bán cho tới khi có chức năng: đơn chụp quyền lợi lúc tạo, nên khách mua lúc này sẽ giữ một gói rỗng đến hết kỳ.
            IsActive = false,
            SortOrder = input.SortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _repository.AddPlan(plan);
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsSubscriptionPlanCreated, actorId, ip, new { planId = plan.Id, plan.Code, plan.MonthlyPriceCents, plan.YearlyPriceCents }, ct);
        return SubscriptionPlanDto.From(plan);
    }

    public async Task<SubscriptionPlanDto> UpdatePlanAsync(Guid actorId, Guid planId, UpdatePlanInput input, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        await _updateValidator.ValidateAndThrowAsync(input, ct);
        var plan = await LoadAsync(planId, ct);

        var monthly = input.RemoveMonthlyPrice ? null : input.MonthlyPriceCents ?? plan.MonthlyPriceCents;
        var yearly = input.RemoveYearlyPrice ? null : input.YearlyPriceCents ?? plan.YearlyPriceCents;
        var isActive = input.IsActive ?? plan.IsActive;
        if (plan.IsDefaultFree)
        {
            if (monthly is not null || yearly is not null || !isActive)
            {
                throw new BusinessRuleException(SubscriptionErrorCodes.FreePlanProtected, "Gói miễn phí không có giá và không tắt được.");
            }
        }
        else if (isActive)
        {
            RequirePrice(monthly, yearly);
            RequireFeature(plan.Features.Any(f => f.Enabled));
        }

        plan.Name = input.Name?.Trim() ?? plan.Name;
        plan.Description = input.Description?.Trim() ?? plan.Description;
        plan.MonthlyPriceCents = monthly;
        plan.YearlyPriceCents = yearly;
        plan.IsActive = isActive;
        plan.SortOrder = input.SortOrder ?? plan.SortOrder;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsSubscriptionPlanUpdated, actorId, ip, new { planId, plan.Code, plan.MonthlyPriceCents, plan.YearlyPriceCents, plan.IsActive }, ct);
        return SubscriptionPlanDto.From(plan);
    }

    public async Task DeletePlanAsync(Guid actorId, Guid planId, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        var plan = await LoadAsync(planId, ct);
        if (plan.IsDefaultFree)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.FreePlanProtected, "Không xóa được gói miễn phí.");
        }

        plan.IsActive = false;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsSubscriptionPlanDeleted, actorId, ip, new { planId, plan.Code }, ct);
    }

    public async Task<SubscriptionPlanDto> SetFeaturesAsync(Guid actorId, Guid planId, IReadOnlyList<PlanFeatureInput> features, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        ValidateFeatures(features);
        var plan = await LoadAsync(planId, ct);
        if (plan.IsActive && !plan.IsDefaultFree)
        {
            RequireFeature(features.Any(f => f.Enabled));
        }

        var existing = plan.Features.ToDictionary(f => f.FeatureKey);
        _repository.RemoveFeatures(plan.Features.Where(f => features.All(i => i.Key != f.FeatureKey)).ToList());
        plan.Features.RemoveAll(f => features.All(i => i.Key != f.FeatureKey));
        foreach (var input in features)
        {
            if (existing.TryGetValue(input.Key, out var feature))
            {
                feature.Enabled = input.Enabled;
                feature.MonthlyLimit = input.Enabled ? input.MonthlyLimit : null;
            }
            else
            {
                plan.Features.Add(new SubscriptionPlanFeature { PlanId = plan.Id, FeatureKey = input.Key, Enabled = input.Enabled, MonthlyLimit = input.Enabled ? input.MonthlyLimit : null });
            }
        }

        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsSubscriptionPlanFeaturesUpdated, actorId, ip, new { planId, plan.Code, features }, ct);
        return SubscriptionPlanDto.From(plan);
    }

    public async Task<OpsSubscriptionOrderPageDto> ListOrdersAsync(Guid actorId, SubscriptionOrderStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        var (items, total) = await _repository.ListOrdersAsync(status, Math.Max(offset, 0), Math.Clamp(limit, 1, MaxPageSize), ct);
        return new OpsSubscriptionOrderPageDto(
            items.Select(o => new OpsSubscriptionOrderDto(o.Id, o.InvoiceNumber, o.PlanCode, o.Cycle, o.AmountCents, o.Status, o.ReviewReason, o.CreatedAt, o.PaidAt)).ToList(),
            total);
    }

    private Task RequireAsync(Guid actorId, CancellationToken ct) => _access.RequireAsync(actorId, OpsCapability.ManageSubscriptionPlans, ct);

    private async Task<SubscriptionPlan> LoadAsync(Guid planId, CancellationToken ct)
        => await _repository.GetPlanForUpdateAsync(planId, ct) ?? throw new NotFoundException("Gói đăng ký", planId);

    private static void RequirePrice(long? monthly, long? yearly)
    {
        if (monthly is null && yearly is null)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.PriceRequired, "Gói đang bán phải có giá theo tháng hoặc theo năm.");
        }
    }

    private static void RequireFeature(bool hasFeature)
    {
        if (!hasFeature)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.PlanHasNoFeatures, "Gói đang bán phải có ít nhất một chức năng.");
        }
    }

    private static void ValidateFeatures(IReadOnlyList<PlanFeatureInput> features)
    {
        if (features.Any(f => !SubscriptionFeatures.IsKnown(f.Key)) || features.Select(f => f.Key).Distinct().Count() != features.Count)
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.UnknownFeature, "Danh sách có chức năng không tồn tại hoặc bị lặp.");
        }

        if (features.Any(f => f.MonthlyLimit < 0))
        {
            throw new BusinessRuleException(SubscriptionErrorCodes.UnknownFeature, "Hạn mức không được âm.");
        }
    }
}
