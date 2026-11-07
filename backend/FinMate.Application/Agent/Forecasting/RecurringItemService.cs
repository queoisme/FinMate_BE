using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Agent;

public sealed record RecurringItemDto(Guid Id, string Name, long AmountVnd, string Kind, int DayOfMonth)
{
    public static RecurringItemDto From(RecurringItem i) => new(i.Id, i.Name, i.AmountCents, WireKind(i.Kind), i.DayOfMonth);

    public static string WireKind(RecurringKind kind) => kind == RecurringKind.Inflow ? "inflow" : "obligation";
}

public sealed record RecurringSuggestionDto(string Name, long AmountVnd, string Kind, int DayOfMonth, int Months);

public sealed record RecurringListDto(IReadOnlyList<RecurringItemDto> Items, IReadOnlyList<RecurringSuggestionDto> Suggestions);

/// <summary>
/// Khoản thu/chi định kỳ: người dùng xác nhận (lưu) hoặc bỏ; hệ thống chỉ ĐỀ XUẤT ứng viên từ <see cref="AgentLimits.RecurringLookbackMonths"/> tháng giao dịch đã xác nhận (không tự lưu).
/// Mọi truy vấn theo <c>userId</c> của lượt/yêu cầu. <see cref="GetEntriesAsync"/> là đầu vào của <c>simulate_purchase</c>: khoản đã lưu là nguồn Confirmed, ứng viên chưa lưu là Estimated.
/// </summary>
public sealed class RecurringItemService(IRecurringItemRepository items, ITransactionRepository transactions)
{
    private const int MaxNameLength = 80;

    public async Task<RecurringListDto> ListAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var (stored, candidates, _) = await LoadAsync(userId, now, ct);
        return new RecurringListDto(
            stored.Select(RecurringItemDto.From).ToList(),
            candidates.Select(c => new RecurringSuggestionDto(c.Name, c.AmountVnd, RecurringItemDto.WireKind(c.Kind), c.DayOfMonth, c.Months)).ToList());
    }

    public async Task<RecurringItemDto> CreateAsync(Guid userId, string? name, long amountVnd, string? kind, int dayOfMonth, CancellationToken ct = default)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw new BusinessRuleException("invalid_recurring_name", $"Tên khoản định kỳ phải có 1–{MaxNameLength} ký tự.");
        }

        if (amountVnd <= 0 || amountVnd > AgentLimits.MaxPriceVnd)
        {
            throw new BusinessRuleException("invalid_recurring_amount", "Số tiền phải là số nguyên đồng dương.");
        }

        if (!Enum.TryParse<RecurringKind>(kind, true, out var parsedKind) || !Enum.IsDefined(parsedKind))
        {
            throw new BusinessRuleException("invalid_recurring_kind", "Loại khoản phải là 'obligation' (phải trả) hoặc 'inflow' (thu).");
        }

        if (dayOfMonth is < 1 or > 31)
        {
            throw new BusinessRuleException("invalid_recurring_day", "Ngày trong tháng phải từ 1 đến 31.");
        }

        if (await items.CountByUserAsync(userId, ct) >= AgentLimits.MaxRecurringItems)
        {
            throw new BusinessRuleException("recurring_limit_reached", $"Chỉ lưu tối đa {AgentLimits.MaxRecurringItems} khoản định kỳ.");
        }

        var item = new RecurringItem { Id = Guid.NewGuid(), UserId = userId, Name = name, AmountCents = amountVnd, Kind = parsedKind, DayOfMonth = dayOfMonth, CreatedAt = DateTimeOffset.UtcNow };
        await items.AddAsync(item, ct);
        return RecurringItemDto.From(item);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        if (!await items.DeleteAsync(id, userId, ct))
        {
            throw new NotFoundException("Khoản định kỳ", id);
        }
    }

    /// <summary>Khoản đã lưu (Confirmed) cộng ứng viên chưa lưu (Estimated), mỗi khoản kèm cờ đã có giao dịch thật trong tháng hiện tại.</summary>
    public async Task<IReadOnlyList<RecurringEntry>> GetEntriesAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var (stored, candidates, history) = await LoadAsync(userId, now, ct);
        var (monthStart, _) = VietnamTime.MonthRange(now);
        var thisMonth = history.Where(t => t.TransactedAt >= monthStart && t.DeletedAt is null).ToList();

        bool Paid(string name, long amount, RecurringKind kind)
        {
            var key = RecurringDetector.NormalizeName(name);
            var type = kind == RecurringKind.Inflow ? TransactionType.Credit : TransactionType.Debit;
            return thisMonth.Any(t => t.TransactionType == type
                                      && RecurringDetector.NormalizeName(t.MerchantName ?? t.Description) == key
                                      && Math.Abs(t.AmountCents - amount) * 100 <= amount * AgentLimits.RecurringAmountTolerancePercent);
        }

        return stored
            .Select(i => new RecurringEntry(i.Name, i.AmountCents, i.Kind, i.DayOfMonth, EvidenceSource.Confirmed, Paid(i.Name, i.AmountCents, i.Kind)))
            .Concat(candidates.Select(c => new RecurringEntry(c.Name, c.AmountVnd, c.Kind, c.DayOfMonth, EvidenceSource.Estimated, Paid(c.Name, c.AmountVnd, c.Kind))))
            .ToList();
    }

    private async Task<(List<RecurringItem> Stored, List<RecurringCandidate> Candidates, List<Transaction> History)> LoadAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await items.GetListByUserAsync(userId, ct);
        var (thisMonthStart, _) = VietnamTime.MonthRange(now);
        var history = await transactions.GetConfirmedSinceAsync(userId, thisMonthStart.AddMonths(-AgentLimits.RecurringLookbackMonths), ct);

        // ứng viên đã được lưu (cùng tên, cùng chiều) thì không đề xuất lại; ứng viên mới KHÔNG tự lưu
        var known = stored.Select(i => (RecurringDetector.NormalizeName(i.Name), i.Kind)).ToHashSet();
        var candidates = RecurringDetector.Detect(history).Where(c => !known.Contains((RecurringDetector.NormalizeName(c.Name), c.Kind))).ToList();
        return (stored, candidates, history);
    }
}
