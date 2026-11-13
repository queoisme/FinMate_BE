using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>
/// Gợi ý chuyển ví tính khi đọc (Phase 83, BR-TRF-05): không lưu danh sách, để ứng viên vừa bị gộp hay bị bác ở chỗ khác không còn hiện ra.
/// Chỉ nháp, và giao dịch đã xác nhận nhưng vừa tạo trong cửa sổ ghép (D10), mới được xét.
/// </summary>
public interface ITransferSuggestions
{
    /// <summary>Khoá = id giao dịch; giao dịch không có gợi ý thì không có khoá. Một truy vấn ứng viên cho cả trang.</summary>
    Task<IReadOnlyDictionary<Guid, TransferSuggestionDto>> ForAsync(Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);

    /// <summary>Như trên cho một giao dịch, trả cả gợi ý thô (để push và thống kê dùng loại AI nhận ra).</summary>
    Task<TransferSuggestion?> ForOneAsync(Guid userId, Transaction transaction, CancellationToken ct = default);
}

public sealed class TransferSuggestions : ITransferSuggestions
{
    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ISystemConfigProvider _config;
    private readonly Func<DateTimeOffset> _now;

    public TransferSuggestions(ITransactionRepository transactions, IFinancialAccountRepository accounts, ISystemConfigProvider config)
        : this(transactions, accounts, config, () => DateTimeOffset.UtcNow)
    {
    }

    internal TransferSuggestions(
        ITransactionRepository transactions, IFinancialAccountRepository accounts, ISystemConfigProvider config, Func<DateTimeOffset> now)
    {
        _transactions = transactions;
        _accounts = accounts;
        _config = config;
        _now = now;
    }

    public async Task<TransferSuggestion?> ForOneAsync(Guid userId, Transaction transaction, CancellationToken ct = default)
        => (await ComputeAsync(userId, [transaction], ct)).Raw.GetValueOrDefault(transaction.Id);

    public async Task<IReadOnlyDictionary<Guid, TransferSuggestionDto>> ForAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
        => (await ComputeAsync(userId, transactions, ct)).Dtos;

    private async Task<(Dictionary<Guid, TransferSuggestion> Raw, Dictionary<Guid, TransferSuggestionDto> Dtos)> ComputeAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct)
    {
        var raw = new Dictionary<Guid, TransferSuggestion>();
        var dtos = new Dictionary<Guid, TransferSuggestionDto>();
        var settings = (await _config.GetAsync(ct)).Document.Transfers;
        var window = TimeSpan.FromMinutes(settings.PairWindowMinutes + settings.ClockSkewMinutes);
        var now = _now();
        var subjects = transactions
            .Where(t => t.TransactionType is TransactionType.Debit or TransactionType.Credit
                && (t.Status == TransactionStatus.Draft || now - t.CreatedAt <= window))
            .ToList();
        if (subjects.Count == 0)
        {
            return (raw, dtos);
        }

        var probes = (await _transactions.ListTransferProbesAsync(
                new TransferProbeQuery(userId, subjects.Min(t => t.TransactedAt) - window, subjects.Max(t => t.TransactedAt) + window), ct))
            .ToDictionary(p => p.Id);
        var accounts = (await _accounts.GetListByUserAsync(userId, ct))
            .Select(a => new TransferAccount(a.Id, a.AccountName, a.AccountType, a.ProviderConfig?.ProviderKey, a.ProviderConfig?.DisplayName))
            .ToList();
        var accountById = accounts.ToDictionary(a => a.Id);

        foreach (var subject in subjects)
        {
            if (!probes.TryGetValue(subject.Id, out var probe)
                || TransferMatcher.Suggest(probe, probes.Values.ToList(), accounts, settings) is not { } suggestion)
            {
                continue;
            }

            raw[subject.Id] = suggestion;
            dtos[subject.Id] = ToDto(suggestion, probes, accountById);
        }

        return (raw, dtos);
    }

    private static TransferSuggestionDto ToDto(
        TransferSuggestion s, IReadOnlyDictionary<Guid, TransferProbe> probes, IReadOnlyDictionary<Guid, TransferAccount> accounts)
    {
        TransferPairedDto? paired = null;
        if (s.PairedId is { } id && probes.TryGetValue(id, out var other))
        {
            paired = new TransferPairedDto(other.Id, other.AmountCents, other.TransactedAt, other.AccountId, accounts.GetValueOrDefault(other.AccountId)?.Name);
        }

        var candidates = s.CandidateAccountIds
            .Where(accounts.ContainsKey)
            .Select(a => new TransferAccountDto(a, accounts[a].Name, accounts[a].Type.ToString()))
            .ToList();
        return new TransferSuggestionDto(s.Kind.ToString(), paired, candidates, s.PairedId is null ? null : s.FeeCents);
    }
}
