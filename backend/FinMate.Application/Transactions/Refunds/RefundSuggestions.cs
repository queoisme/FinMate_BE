using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Refunds;

/// <summary>
/// Gợi ý khoản chi gốc cho một khoản tiền vào mà AI đoán là hoàn/đảo (Phase 81, luồng tự động bước 2–3). CHỈ gợi ý: không bao giờ tự liên kết
/// hay tự xác nhận (BR-RF-13). Tính khi đọc, không lưu, để ứng viên vừa được hoàn hết ở chỗ khác không còn hiện ra (D6).
/// </summary>
public interface IRefundSuggestions
{
    Task<IReadOnlyList<RefundSuggestionDto>> SuggestAsync(Guid userId, Transaction moneyIn, TransactionRelation hint, CancellationToken ct = default);

    /// <summary>Gợi ý cho các NHÁP tiền vào có hint (đọc từ kết quả AI đã lưu). Khoá = id giao dịch; nháp không có hint thì không có trong kết quả.</summary>
    Task<IReadOnlyDictionary<Guid, (TransactionRelation Hint, IReadOnlyList<RefundSuggestionDto> Items)>> ForDraftsAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);

    /// <summary>Đọc chuỗi hint từ AI Service; giá trị lạ (hợp đồng mới hơn backend) bị bỏ qua.</summary>
    static TransactionRelation? Parse(string? hint) => hint?.ToLowerInvariant() switch
    {
        "refund" => TransactionRelation.Refund,
        "reversal" => TransactionRelation.Reversal,
        _ => null,
    };
}

public sealed class RefundSuggestions : IRefundSuggestions
{
    private readonly ITransactionRepository _transactions;
    private readonly INotificationLogRepository _logs;
    private readonly ISystemConfigProvider _config;

    public RefundSuggestions(ITransactionRepository transactions, INotificationLogRepository logs, ISystemConfigProvider config)
    {
        _transactions = transactions;
        _logs = logs;
        _config = config;
    }

    public async Task<IReadOnlyList<RefundSuggestionDto>> SuggestAsync(Guid userId, Transaction moneyIn, TransactionRelation hint, CancellationToken ct = default)
    {
        if (moneyIn.TransactionType != TransactionType.Credit)
        {
            return [];
        }

        var settings = (await _config.GetAsync(ct)).Document.Refunds;
        var window = hint == TransactionRelation.Reversal ? settings.ReversalMatchWindowDays : settings.MatchWindowDays;
        var candidates = await _transactions.FindRefundCandidatesAsync(
            new RefundCandidateQuery(
                userId,
                moneyIn.AmountCents,
                moneyIn.TransactedAt,
                window,
                moneyIn.FinancialAccountId,
                moneyIn.FinancialAccount?.ProviderConfigId,
                CounterpartyKey.From(moneyIn.MerchantName),
                settings.MaxSuggestions,
                moneyIn.Id == Guid.Empty ? null : moneyIn.Id),
            ct);

        return candidates
            .Select(c => new RefundSuggestionDto(
                c.Original.Id,
                c.Original.AmountCents,
                c.RefundedCents,
                c.RemainingRefundableCents,
                c.Original.MerchantName,
                c.Original.CategoryId,
                c.Original.Category?.Name,
                c.Original.FinancialAccountId,
                c.Original.TransactedAt))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, (TransactionRelation Hint, IReadOnlyList<RefundSuggestionDto> Items)>> ForDraftsAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var drafts = transactions
            .Where(t => t.Status == TransactionStatus.Draft && t.TransactionType == TransactionType.Credit && t.RelatedTransactionId is null && t.NotificationLogId is not null)
            .ToList();
        var result = new Dictionary<Guid, (TransactionRelation, IReadOnlyList<RefundSuggestionDto>)>();
        if (drafts.Count == 0)
        {
            return result;
        }

        var hints = await _logs.GetRelationHintsAsync(userId, drafts.Select(d => d.NotificationLogId!.Value).ToList(), ct);
        foreach (var draft in drafts)
        {
            if (hints.TryGetValue(draft.NotificationLogId!.Value, out var hint))
            {
                result[draft.Id] = (hint, await SuggestAsync(userId, draft, hint, ct));
            }
        }

        return result;
    }
}
