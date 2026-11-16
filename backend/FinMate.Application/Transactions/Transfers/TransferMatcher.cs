using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>Ảnh chụp một giao dịch đủ để xét chuyển ví. Hint/kind là của AI (<c>ai_results</c>) cho giao dịch đến từ thông báo.</summary>
public sealed record TransferProbe(
    Guid Id,
    TransactionType Type,
    TransactionStatus Status,
    long AmountCents,
    DateTimeOffset TransactedAt,
    DateTimeOffset CreatedAt,
    Guid AccountId,
    string? MerchantName,
    string? Description,
    NotificationRelationHint? Hint,
    TransferKind? Kind,
    TransactionRelation? Relation = null,
    bool IsReversed = false,
    bool IsDeletedOrMerged = false,
    DuplicateStatus? DuplicateStatus = null,
    Guid? GroupId = null,
    bool QrMatched = false,
    bool HasConfirmedRefunds = false,
    DateTimeOffset? TransferDismissedAt = null);

/// <summary>Một ví của người dùng. <paramref name="ProviderKey"/>/<paramref name="ProviderName"/> null với ví tự tạo (Tiền mặt).</summary>
public sealed record TransferAccount(Guid Id, string Name, AccountType Type, string? ProviderKey, string? ProviderName);

/// <param name="Kind">Loại chuyển: của AI nếu có, không thì suy từ loại hai ví.</param>
/// <param name="HintedKind">Loại AI nhận ra; null khi chỉ ghép nhờ tên ví (thống kê "unhinted").</param>
/// <param name="PairedId">Chiều bên kia khi ghép hai chiều; null khi gợi ý một chiều.</param>
/// <param name="FromAccountId">Ví nguồn đã biết (null khi người dùng phải chọn).</param>
/// <param name="ToAccountId">Ví đích đã biết (null khi người dùng phải chọn).</param>
/// <param name="AmountCents">Số tiền của Transfer (chiều tiền vào khi ghép, hoặc chính giao dịch).</param>
/// <param name="FeeCents">Phần chênh tiền ra − tiền vào khi ghép; null khi một chiều.</param>
/// <param name="CandidateAccountIds">Ví người dùng có thể chọn khi một chiều; rỗng khi đã ghép.</param>
public sealed record TransferSuggestion(
    TransferKind Kind,
    TransferKind? HintedKind,
    Guid? PairedId,
    Guid? FromAccountId,
    Guid? ToAccountId,
    long AmountCents,
    long? FeeCents,
    IReadOnlyList<Guid> CandidateAccountIds);

/// <summary>Lý do một giao dịch không chuyển thành Transfer được (BR-TRF-04).</summary>
public enum TransferBlock
{
    NotDebitOrCredit,
    Relation,
    Reversed,
    Merged,
    SuspectedDuplicate,
    QrMatched,
    Shared,
    HasRefunds,
}

/// <summary>
/// Quyết định một khoản chi/thu có thể là tiền chuyển giữa các ví của chính người dùng (Phase 83). Hàm thuần: repository nạp ứng viên thô, hàm này
/// áp BR-TRF-02/03/04. Chỉ GỢI Ý: không có tín hiệu (từ khoá của AI, hoặc nội dung nhắc tên ví bên kia) thì không gợi ý, kể cả khi số tiền và
/// thời gian khớp — "chuyển cho bạn 500k" và "người khác chuyển cho mình 500k" không được thành một Transfer.
/// </summary>
public static class TransferMatcher
{
    /// <summary>
    /// Vì sao không chuyển đổi được (null = được). Cờ "không phải chuyển ví" KHÔNG chặn chuyển đổi tay (S2), chỉ chặn gợi ý (<see cref="Suggest"/>).
    /// </summary>
    public static TransferBlock? BlockForConversion(TransferProbe p)
    {
        if (p.Type is not (TransactionType.Debit or TransactionType.Credit))
        {
            return TransferBlock.NotDebitOrCredit;
        }

        if (p.Relation is not null)
        {
            return TransferBlock.Relation;
        }

        if (p.IsReversed)
        {
            return TransferBlock.Reversed;
        }

        if (p.IsDeletedOrMerged)
        {
            return TransferBlock.Merged;
        }

        if (p.DuplicateStatus == DuplicateStatus.Suspected)
        {
            return TransferBlock.SuspectedDuplicate;
        }

        if (p.QrMatched)
        {
            return TransferBlock.QrMatched;
        }

        if (p.GroupId is not null)
        {
            return TransferBlock.Shared;
        }

        return p.HasConfirmedRefunds ? TransferBlock.HasRefunds : null;
    }

    public static TransferSuggestion? Suggest(
        TransferProbe subject, IReadOnlyCollection<TransferProbe> others, IReadOnlyList<TransferAccount> accounts, TransferSettings settings)
    {
        if (!CanBeSuggested(subject))
        {
            return null;
        }

        var byId = accounts.ToDictionary(a => a.Id);
        var pair = others
            .Where(o => o.Id != subject.Id && CanBeSuggested(o) && o.Type != subject.Type)
            .Select(o => subject.Type == TransactionType.Debit ? (Debit: subject, Credit: o) : (Debit: o, Credit: subject))
            .Where(p => IsPair(p.Debit, p.Credit, byId, settings))
            .OrderBy(p => p.Debit.AmountCents - p.Credit.AmountCents)
            .ThenBy(p => (p.Credit.TransactedAt - p.Debit.TransactedAt).Duration())
            .ThenBy(p => (subject.Type == TransactionType.Debit ? p.Credit : p.Debit).Id)
            .Select(p => ((TransferProbe Debit, TransferProbe Credit)?)p)
            .FirstOrDefault();

        if (pair is { } found)
        {
            var hinted = Hinted(subject) ?? Hinted(found.Debit == subject ? found.Credit : found.Debit);
            return new TransferSuggestion(
                hinted ?? Infer(byId.GetValueOrDefault(found.Debit.AccountId), byId.GetValueOrDefault(found.Credit.AccountId)),
                hinted,
                found.Debit == subject ? found.Credit.Id : found.Debit.Id,
                found.Debit.AccountId,
                found.Credit.AccountId,
                found.Credit.AmountCents,
                found.Debit.AmountCents - found.Credit.AmountCents,
                []);
        }

        return subject.Hint == NotificationRelationHint.Transfer ? OneSided(subject, accounts) : null;
    }

    /// <summary>BR-TRF-03: không tìm thấy chiều bên kia trong cửa sổ, nhưng AI nói đây là chuyển ví → cho người dùng chọn ví còn lại.</summary>
    private static TransferSuggestion OneSided(TransferProbe subject, IReadOnlyList<TransferAccount> accounts)
    {
        var kind = subject.Kind ?? TransferKind.OwnAccount;
        var cashOnly = kind is TransferKind.AtmWithdrawal or TransferKind.AtmDeposit;
        var candidates = accounts
            .Where(a => a.Id != subject.AccountId && (!cashOnly || a.Type == AccountType.Cash))
            .Select(a => a.Id)
            .ToList();
        var outgoing = subject.Type == TransactionType.Debit;
        return new TransferSuggestion(
            kind, subject.Kind, null, outgoing ? subject.AccountId : null, outgoing ? null : subject.AccountId, subject.AmountCents, null, candidates);
    }

    private static bool CanBeSuggested(TransferProbe p) => BlockForConversion(p) is null && p.TransferDismissedAt is null;

    private static bool IsPair(TransferProbe debit, TransferProbe credit, IReadOnlyDictionary<Guid, TransferAccount> accounts, TransferSettings settings)
    {
        if (debit.AccountId == credit.AccountId)
        {
            return false;
        }

        var lag = credit.TransactedAt - debit.TransactedAt;
        if (lag < -TimeSpan.FromMinutes(settings.ClockSkewMinutes) || lag > TimeSpan.FromMinutes(settings.PairWindowMinutes))
        {
            return false;
        }

        var fee = debit.AmountCents - credit.AmountCents;
        if (fee < 0 || fee > settings.MaxFeeCents || fee * 100 > credit.AmountCents * settings.MaxFeePercent)
        {
            return false;
        }

        return debit.Hint == NotificationRelationHint.Transfer
               || credit.Hint == NotificationRelationHint.Transfer
               || Mentions(debit, accounts.GetValueOrDefault(credit.AccountId), settings)
               || Mentions(credit, accounts.GetValueOrDefault(debit.AccountId), settings);
    }

    /// <summary>D5: tên nơi chi / mô tả của chiều này nhắc tên provider của ví bên kia (nguyên từ, không dấu, không phân biệt hoa thường).</summary>
    private static bool Mentions(TransferProbe side, TransferAccount? other, TransferSettings settings)
    {
        if (other?.ProviderKey is not { } key || CounterpartyKey.From($"{side.MerchantName} {side.Description}") is not { } text)
        {
            return false;
        }

        var aliases = settings.ProviderAliases.TryGetValue(key, out var configured) ? configured : [];
        var padded = $" {text} ";
        return aliases.Append(key).Append(other.ProviderName ?? string.Empty)
            .Select(CounterpartyKey.From)
            .Any(alias => alias is { Length: >= 2 } && padded.Contains($" {alias} ", StringComparison.Ordinal));
    }

    private static TransferKind? Hinted(TransferProbe p) => p.Hint == NotificationRelationHint.Transfer ? p.Kind ?? TransferKind.OwnAccount : null;

    /// <summary>Không có loại từ AI: suy từ loại hai ví (ngân hàng → ví điện tử là nạp ví, ngân hàng → tiền mặt là rút ATM…).</summary>
    private static TransferKind Infer(TransferAccount? from, TransferAccount? to) => (from?.Type, to?.Type) switch
    {
        (AccountType.Bank, AccountType.EWallet) => TransferKind.WalletTopup,
        (AccountType.EWallet, AccountType.Bank) => TransferKind.WalletWithdrawal,
        (AccountType.Bank, AccountType.Cash) => TransferKind.AtmWithdrawal,
        (AccountType.Cash, AccountType.Bank) => TransferKind.AtmDeposit,
        _ => TransferKind.OwnAccount,
    };
}
