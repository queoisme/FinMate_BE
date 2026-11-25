using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>Phần sổ vay của một giao dịch trong DTO: đang gắn khoản nào (<see cref="Link"/>) hoặc gợi ý khoản nào (<see cref="Suggestions"/>).</summary>
public sealed record LoanTransactionViewDto(LoanLinkDto? Link, IReadOnlyList<LoanSuggestionDto>? Suggestions);

/// <summary>
/// Liên kết và gợi ý khoản vay, tính khi đọc (Phase 90, BR-DEBT-13, D9): không lưu danh sách gợi ý, nên khoản vừa trả xong ở chỗ khác không còn hiện ra.
/// Một truy vấn liên kết, một truy vấn khoản <c>open</c> và một truy vấn gợi ý AI cho cả trang. Không bao giờ tự liên kết. Phía vận hành không dùng
/// dịch vụ này (S4 của Phase 86 cũ): tên người là dữ liệu bên thứ ba.
/// </summary>
public interface ILoanTransactionView
{
    /// <summary>Khoá = id giao dịch; giao dịch không gắn và không có gợi ý thì không có khoá.</summary>
    Task<IReadOnlyDictionary<Guid, LoanTransactionViewDto>> ViewAsync(Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);

    /// <summary>Gợi ý cho một giao dịch vừa tạo mà gợi ý AI đang có trong tay (push của luồng thông báo, D22), không đọc lại gợi ý từ DB.</summary>
    Task<IReadOnlyList<LoanSuggestionDto>> SuggestForAsync(Guid userId, Transaction transaction, DebtHintStrength? hint, CancellationToken ct = default);
}

public sealed class LoanTransactionViewService : ILoanTransactionView
{
    private readonly ILoanRepository _loans;
    private readonly INotificationLogRepository _logs;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public LoanTransactionViewService(ILoanRepository loans, INotificationLogRepository logs, ISystemConfigProvider config)
        : this(loans, logs, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal LoanTransactionViewService(ILoanRepository loans, INotificationLogRepository logs, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _loans = loans;
        _logs = logs;
        _config = config;
        _now = now;
    }

    public async Task<IReadOnlyDictionary<Guid, LoanTransactionViewDto>> ViewAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, LoanTransactionViewDto>();
        if (transactions.Count == 0)
        {
            return result;
        }

        var links = await _loans.GetLinksAsync(userId, transactions.Select(t => t.Id).ToList(), ct);
        foreach (var (id, link) in links)
        {
            result[id] = new LoanTransactionViewDto(link, null);
        }

        var settings = (await _config.GetAsync(ct)).Document.Loans;
        var subjects = transactions.Where(t => !links.ContainsKey(t.Id) && IsSubject(t, settings.SuggestWindowHours)).ToList();
        if (subjects.Count == 0)
        {
            return result;
        }

        var open = await _loans.ListOpenAsync(userId, ct);
        if (open.Count == 0)
        {
            return result;
        }

        var logIds = subjects.Where(t => t.NotificationLogId is not null).Select(t => t.NotificationLogId!.Value).ToList();
        var hints = await _logs.GetDebtHintsAsync(userId, logIds, ct);
        var rules = Rules(settings);
        foreach (var t in subjects)
        {
            DebtHintStrength? hint = t.NotificationLogId is { } logId && hints.TryGetValue(logId, out var h) ? h : null;
            var ranked = LoanSuggestionRanker.Rank(new LoanSuggestionInput(t.TransactionType, t.AmountCents, TextOf(t), hint), open, rules);
            if (ranked.Count > 0)
            {
                result[t.Id] = new LoanTransactionViewDto(null, ranked);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<LoanSuggestionDto>> SuggestForAsync(Guid userId, Transaction transaction, DebtHintStrength? hint, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Loans;
        if (!IsSubject(transaction, settings.SuggestWindowHours))
        {
            return [];
        }

        var open = await _loans.ListOpenAsync(userId, ct);
        return open.Count == 0
            ? []
            : LoanSuggestionRanker.Rank(new LoanSuggestionInput(transaction.TransactionType, transaction.AmountCents, TextOf(transaction), hint), open, Rules(settings));
    }

    private LoanSuggestionRules Rules(Admin.SystemConfig.LoanSettings settings)
        => new(VietnamTime.DateOf(_now()), settings.InterestToleranceCents, settings.Honorifics, settings.MaxSuggestions);

    /// <summary>D9: nháp, hoặc đã xác nhận trong cửa sổ gợi ý; đủ điều kiện BR-DEBT-08 trừ trạng thái (nháp gắn qua <c>confirm</c> kèm <c>loanId</c>, D4).</summary>
    private bool IsSubject(Transaction t, int windowHours)
        => LoanPolicy.IsEligible(t, beingConfirmed: true)
           && (t.Status == TransactionStatus.Draft || _now() - t.CreatedAt <= TimeSpan.FromHours(windowHours));

    private static string TextOf(Transaction t) => string.Join(' ', new[] { t.MerchantName, t.Description }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
