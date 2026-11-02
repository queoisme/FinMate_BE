using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Forecasting;

public sealed record RecurringItemDto(
    Guid Id,
    string Direction,
    string Name,
    string? CounterpartyKey,
    Guid? CategoryId,
    Guid? FinancialAccountId,
    long AmountCents,
    string Cadence,
    DateOnly NextDueDate,
    string Status,
    string Source,
    int Occurrences,
    DateTimeOffset? LastSeenAt,
    long? LastSeenAmountCents,
    DateTimeOffset CreatedAt);

/// <param name="CounterpartyName">Tên người nhận/người trả như trên giao dịch (vd "Netflix"), để khớp giao dịch thật. Bỏ trống thì khoản không tự khớp được.</param>
public sealed record CreateRecurringItemCommand(
    Guid UserId,
    RecurringDirection Direction,
    string Name,
    string? CounterpartyName,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId);

/// <summary>Trường null = giữ nguyên.</summary>
public sealed record UpdateRecurringItemCommand(
    Guid UserId,
    Guid Id,
    string? Name,
    long? AmountCents,
    RecurringCadence? Cadence,
    DateOnly? NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId);

public interface IRecurringItemService
{
    Task<IReadOnlyList<RecurringItemDto>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default);
    Task<RecurringItemDto> CreateAsync(CreateRecurringItemCommand command, CancellationToken ct = default);
    Task<RecurringItemDto> UpdateAsync(UpdateRecurringItemCommand command, CancellationToken ct = default);
    Task<RecurringItemDto> ConfirmAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<RecurringItemDto> RejectAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<RecurringItemDto> PauseAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<RecurringItemDto> ResumeAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Hủy khoản (giữ dòng ở trạng thái đã hủy để bộ phát hiện không đề xuất lại). Gọi lại với khoản đã hủy/đã từ chối vẫn thành công.</summary>
    Task CancelAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class RecurringItemService : IRecurringItemService
{
    private readonly IRecurringItemRepository _items;
    private readonly ICategoryRepository _categories;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ICacheService _cache;
    private readonly IValidator<CreateRecurringItemCommand> _createValidator;
    private readonly IValidator<UpdateRecurringItemCommand> _updateValidator;

    public RecurringItemService(
        IRecurringItemRepository items,
        ICategoryRepository categories,
        IFinancialAccountRepository accounts,
        ICacheService cache,
        IValidator<CreateRecurringItemCommand> createValidator,
        IValidator<UpdateRecurringItemCommand> updateValidator)
    {
        _items = items;
        _categories = categories;
        _accounts = accounts;
        _cache = cache;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<RecurringItemDto>> ListAsync(Guid userId, RecurringStatus? status, CancellationToken ct = default)
        => (await _items.ListAsync(userId, status, ct)).OrderBy(i => i.NextDueDate).Select(ToDto).ToList();

    public async Task<RecurringItemDto> CreateAsync(CreateRecurringItemCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        await EnsureReferencesAsync(command.UserId, command.CategoryId, command.FinancialAccountId, ct);

        var key = CounterpartyKey.From(command.CounterpartyName);
        var now = DateTimeOffset.UtcNow;
        var existing = key is null ? null : await _items.FindByCounterpartyAsync(command.UserId, command.Direction, key, ct);
        if (existing is { Status: RecurringStatus.Active or RecurringStatus.Paused })
        {
            throw new ConflictException(ForecastErrorCodes.RecurringDuplicate, "Bạn đã có khoản định kỳ cho người nhận này.");
        }

        // Người nhận đã từng bị từ chối/hủy hoặc đang được đề xuất: dùng lại dòng đó (khóa duy nhất theo người nhận) với thông tin user vừa nhập.
        var item = existing ?? new RecurringItem { Id = Guid.NewGuid(), UserId = command.UserId, CreatedAt = now };
        item.Direction = command.Direction;
        item.Name = command.Name.Trim();
        item.CounterpartyKey = key;
        item.AmountCents = command.AmountCents;
        item.Cadence = command.Cadence;
        item.NextDueDate = command.NextDueDate;
        item.CategoryId = command.CategoryId;
        item.FinancialAccountId = command.FinancialAccountId;
        item.Status = RecurringStatus.Active;
        item.Source = RecurringSource.Manual;
        item.UpdatedAt = now;

        if (existing is null)
        {
            await _items.AddAsync(item, ct);
        }
        else
        {
            await _items.UpdateAsync(item, ct);
        }

        await InvalidateAsync(command.UserId, ct);
        return ToDto(item);
    }

    public async Task<RecurringItemDto> UpdateAsync(UpdateRecurringItemCommand command, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(command, ct);
        var item = await GetOwnedAsync(command.UserId, command.Id, ct);
        await EnsureReferencesAsync(command.UserId, command.CategoryId, command.FinancialAccountId, ct);

        item.Name = command.Name?.Trim() ?? item.Name;
        item.AmountCents = command.AmountCents ?? item.AmountCents;
        item.Cadence = command.Cadence ?? item.Cadence;
        item.NextDueDate = command.NextDueDate ?? item.NextDueDate;
        item.CategoryId = command.CategoryId ?? item.CategoryId;
        item.FinancialAccountId = command.FinancialAccountId ?? item.FinancialAccountId;
        return await SaveAsync(item, ct);
    }

    public Task<RecurringItemDto> ConfirmAsync(Guid userId, Guid id, CancellationToken ct = default)
        => TransitionAsync(userId, id, RecurringStatus.Suggested, RecurringStatus.Active, ct);

    public Task<RecurringItemDto> RejectAsync(Guid userId, Guid id, CancellationToken ct = default)
        => TransitionAsync(userId, id, RecurringStatus.Suggested, RecurringStatus.Rejected, ct);

    public Task<RecurringItemDto> PauseAsync(Guid userId, Guid id, CancellationToken ct = default)
        => TransitionAsync(userId, id, RecurringStatus.Active, RecurringStatus.Paused, ct);

    public Task<RecurringItemDto> ResumeAsync(Guid userId, Guid id, CancellationToken ct = default)
        => TransitionAsync(userId, id, RecurringStatus.Paused, RecurringStatus.Active, ct);

    public async Task CancelAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var item = await GetOwnedAsync(userId, id, ct);
        if (item.Status is RecurringStatus.Cancelled or RecurringStatus.Rejected)
        {
            return;
        }

        item.Status = RecurringStatus.Cancelled;
        await SaveAsync(item, ct);
    }

    private async Task<RecurringItemDto> TransitionAsync(Guid userId, Guid id, RecurringStatus from, RecurringStatus to, CancellationToken ct)
    {
        var item = await GetOwnedAsync(userId, id, ct);
        if (item.Status == to)
        {
            return ToDto(item);
        }

        if (item.Status != from)
        {
            throw new ConflictException(
                ForecastErrorCodes.RecurringState,
                $"Không thể chuyển khoản định kỳ từ '{RecurringItemMapping.Text(item.Status)}' sang '{RecurringItemMapping.Text(to)}'.");
        }

        item.Status = to;
        return await SaveAsync(item, ct);
    }

    private async Task<RecurringItemDto> SaveAsync(RecurringItem item, CancellationToken ct)
    {
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _items.UpdateAsync(item, ct);
        await InvalidateAsync(item.UserId, ct);
        return ToDto(item);
    }

    private async Task<RecurringItem> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct)
        => await _items.GetAsync(id, userId, ct) ?? throw new NotFoundException("RecurringItem", id);

    private async Task EnsureReferencesAsync(Guid userId, Guid? categoryId, Guid? accountId, CancellationToken ct)
    {
        if (categoryId is { } cid && !IsUsable(await _categories.GetByIdAsync(cid, ct), userId))
        {
            throw new BusinessRuleException(ForecastErrorCodes.RecurringReferenceInvalid, "Danh mục không tồn tại.");
        }

        if (accountId is { } aid && await _accounts.GetByIdAsync(aid, userId, ct) is null)
        {
            throw new BusinessRuleException(ForecastErrorCodes.RecurringReferenceInvalid, "Ví không tồn tại.");
        }
    }

    /// <summary>Danh mục hệ thống, hoặc danh mục riêng của chính user; đã xóa thì không dùng được.</summary>
    private static bool IsUsable(Category? category, Guid userId)
        => category is { DeletedAt: null } && (category.IsSystem || category.UserId == userId);

    private Task InvalidateAsync(Guid userId, CancellationToken ct)
        => _cache.RemoveAsync(CacheKeys.ForecastBaseline(userId, VietnamTime.Today()), ct);

    private static RecurringItemDto ToDto(RecurringItem i) => new(
        i.Id,
        RecurringItemMapping.Text(i.Direction),
        i.Name,
        i.CounterpartyKey,
        i.CategoryId,
        i.FinancialAccountId,
        i.AmountCents,
        RecurringItemMapping.Text(i.Cadence),
        i.NextDueDate,
        RecurringItemMapping.Text(i.Status),
        RecurringItemMapping.Text(i.Source),
        i.Occurrences,
        i.LastSeenAt,
        i.LastSeenAmountCents,
        i.CreatedAt);
}
