using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Duplicates;

/// <summary>
/// Cầu nối giữa <see cref="DuplicateMatcher"/> (hàm thuần) và dữ liệu: nạp ứng viên của user, dựng tập cặp đã bị bác, đọc cửa sổ từ cấu hình
/// hệ thống (nhóm <c>duplicates</c>). Mọi kênh tạo giao dịch (thông báo, job thử lại, chat, hoá đơn, nhập tay) đi qua đây (BR-DUP-02).
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>
    /// So một giao dịch mới (chưa hoặc vừa lưu) với các giao dịch hiện có. Có ứng viên gắn nghi trùng → đặt <c>DuplicateOfTransactionId</c>
    /// = hạng 1 và <c>DuplicateStatus = Suspected</c> trên <paramref name="transaction"/> (BR-DUP-06); caller tự lưu.
    /// <see cref="DuplicateMatch.SameEventId"/> có giá trị thì không đụng gì: caller bỏ thông báo (BR-DUP-05).
    /// </summary>
    /// <param name="packageName">Package của thông báo đã sinh ra giao dịch, null với các kênh khác.</param>
    /// <param name="aiHintLogId">Notification log mà AI Service cho là bản gốc (<c>duplicate_request_id</c>), chỉ để phân định hạng (D14).</param>
    Task<DuplicateMatch> MarkSuspectedAsync(Transaction transaction, string? packageName, Guid? aiHintLogId = null, CancellationToken ct = default);

    /// <summary>
    /// Ứng viên cho một khoản CHƯA lưu (bản nháp của chat, một lần quét hoá đơn): không đụng gì trong DB, chỉ trả danh sách để app hiện.
    /// <see cref="DuplicateSuggestion.MarksSuspected"/> = khoản này khi lưu sẽ mang nghi trùng, nên chat không được tự lưu nó (BR-DUP-06).
    /// </summary>
    Task<DuplicateSuggestion> SuggestForInputAsync(DuplicateInput input, CancellationToken ct = default);

    /// <summary>Mô tả các giao dịch theo id, giữ đúng thứ tự đưa vào; id không còn (đã xoá, của người khác) bị bỏ.</summary>
    Task<IReadOnlyList<DuplicateSummaryDto>> DescribeAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Phần nghi trùng của DTO cho một trang giao dịch (hai truy vấn cho cả trang): giao dịch mà mỗi dòng trỏ tới, và gợi ý trùng cho các NHÁP.
    /// Khoá = id giao dịch; dòng không có gì để hiện thì không có khoá.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DuplicateView>> ViewAsync(Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);

    /// <summary>Ứng viên tính khi đọc cho nhiều giao dịch của cùng user (một truy vấn). Khoá = id giao dịch; không có ứng viên thì không có khoá.</summary>
    Task<IReadOnlyDictionary<Guid, DuplicateMatch>> FindForManyAsync(Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);
}

/// <summary>Một khoản người dùng sắp ghi (chưa có trong DB). <paramref name="AccountId"/> null khi chưa chọn được ví.</summary>
public sealed record DuplicateInput(
    Guid UserId, TransactionSource Source, TransactionType Type, long AmountCents, DateTimeOffset At, Guid? AccountId, string? MerchantName);

/// <param name="Items">Tối đa 3 ứng viên, hạng 1 trước.</param>
public sealed record DuplicateSuggestion(IReadOnlyList<DuplicateSummaryDto> Items, bool MarksSuspected)
{
    public static readonly DuplicateSuggestion None = new([], false);
}

public sealed class DuplicateDetector : IDuplicateDetector
{
    private readonly ITransactionRepository _transactions;
    private readonly ISystemConfigProvider _config;

    public DuplicateDetector(ITransactionRepository transactions, ISystemConfigProvider config)
    {
        _transactions = transactions;
        _config = config;
    }

    public static DuplicateProbe ProbeOf(Transaction t, string? packageName) => new(
        t.Id,
        t.Source,
        t.TransactionType,
        t.AmountCents,
        t.TransactedAt,
        t.CreatedAt,
        t.FinancialAccountId,
        t.MerchantName,
        t.BalanceAfterCents,
        packageName ?? t.NotificationLog?.PackageName,
        t.Relation,
        t.DeletedAt is not null,
        t.MergedIntoTransactionId is not null,
        t.MergedSources,
        t.NotificationLogId);

    public async Task<DuplicateMatch> MarkSuspectedAsync(
        Transaction transaction, string? packageName, Guid? aiHintLogId = null, CancellationToken ct = default)
    {
        var subject = ProbeOf(transaction, packageName);
        if (!DuplicateMatcher.CanHaveDuplicates(subject))
        {
            return DuplicateMatch.None;
        }

        var document = (await _config.GetAsync(ct)).Document;
        var settings = document.Duplicates;
        var window = DuplicateMatcher.WidestWindow(settings);
        var rows = await _transactions.ListDuplicateCandidatesAsync(
            new DuplicateCandidateQuery(transaction.UserId, [transaction.AmountCents], subject.TransactedAt - window, subject.TransactedAt + window,
                TransferAmountMin: transaction.AmountCents - document.Transfers.MaxFeeCents, TransferAmountMax: transaction.AmountCents),
            ct);

        var match = DuplicateMatcher.Match(subject, Probes(rows), DismissedWith(subject.Id, rows), settings, aiHintLogId, document.Transfers);
        if (match.SameEventId is null && match.SuspectedOf is { } top)
        {
            transaction.DuplicateOfTransactionId = top.Id;
            transaction.DuplicateStatus = DuplicateStatus.Suspected;
        }

        return match;
    }

    public async Task<DuplicateSuggestion> SuggestForInputAsync(DuplicateInput input, CancellationToken ct = default)
    {
        if (input.AmountCents <= 0)
        {
            return DuplicateSuggestion.None;
        }

        // Bản dựng tạm, không bao giờ lưu: chỉ để hỏi bộ so khớp như thể khoản này đã được ghi.
        var probe = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = input.UserId,
            FinancialAccountId = input.AccountId ?? Guid.Empty,
            AmountCents = input.AmountCents,
            TransactionType = input.Type,
            Source = input.Source,
            MerchantName = input.MerchantName,
            TransactedAt = input.At,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var match = await MarkSuspectedAsync(probe, null, null, ct);
        if (match.Candidates.Count == 0)
        {
            return DuplicateSuggestion.None;
        }

        var items = await DescribeAsync(input.UserId, match.Candidates.Select(c => c.Id).ToList(), ct);
        return new DuplicateSuggestion(items, match.SuspectedOf is not null);
    }

    public async Task<IReadOnlyList<DuplicateSummaryDto>> DescribeAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var byId = (await _transactions.ListByIdsAsync(userId, ids, ct)).ToDictionary(t => t.Id);
        return ids.Where(byId.ContainsKey).Select(id => Describe(byId[id])).ToList();
    }

    public static DuplicateSummaryDto Describe(Transaction t) => new(
        t.Id, t.AmountCents, t.TransactedAt, t.Source.ToString(), t.MerchantName, t.Category?.Name, t.FinancialAccount?.AccountName);

    public async Task<IReadOnlyDictionary<Guid, DuplicateView>> ViewAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var drafts = transactions.Where(t => t.Status == TransactionStatus.Draft).ToList();
        var matches = drafts.Count == 0 ? new Dictionary<Guid, DuplicateMatch>() : await FindForManyAsync(userId, drafts, ct);

        var linked = transactions.Where(t => t.DuplicateOfTransactionId is not null).Select(t => t.DuplicateOfTransactionId!.Value);
        var ids = linked.Concat(matches.Values.SelectMany(m => m.Candidates.Select(c => c.Id))).Distinct().ToList();
        var byId = ids.Count == 0
            ? new Dictionary<Guid, DuplicateSummaryDto>()
            : (await _transactions.ListByIdsAsync(userId, ids, ct)).ToDictionary(t => t.Id, Describe);

        var result = new Dictionary<Guid, DuplicateView>();
        foreach (var t in transactions)
        {
            var of = t.DuplicateOfTransactionId is { } target ? byId.GetValueOrDefault(target) : null;
            var suggested = matches.TryGetValue(t.Id, out var match)
                ? match.Candidates.Where(c => byId.ContainsKey(c.Id)).Select(c => byId[c.Id]).ToList()
                : null;
            if (of is not null || suggested is { Count: > 0 })
            {
                result[t.Id] = new DuplicateView(of, suggested is { Count: > 0 } ? suggested : null);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, DuplicateMatch>> FindForManyAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var subjects = transactions.Select(t => ProbeOf(t, null)).Where(DuplicateMatcher.CanHaveDuplicates).ToList();
        if (subjects.Count == 0)
        {
            return new Dictionary<Guid, DuplicateMatch>();
        }

        var document = (await _config.GetAsync(ct)).Document;
        var settings = document.Duplicates;
        var window = DuplicateMatcher.WidestWindow(settings);
        var rows = await _transactions.ListDuplicateCandidatesAsync(
            new DuplicateCandidateQuery(
                userId,
                subjects.Select(s => s.AmountCents).Distinct().ToList(),
                subjects.Min(s => s.TransactedAt) - window,
                subjects.Max(s => s.TransactedAt) + window,
                subjects.Min(s => s.AmountCents) - document.Transfers.MaxFeeCents,
                subjects.Max(s => s.AmountCents)),
            ct);
        var probes = Probes(rows);

        var result = new Dictionary<Guid, DuplicateMatch>();
        foreach (var subject in subjects)
        {
            // Lúc đọc chỉ cần danh sách ứng viên; "cùng sự kiện" chỉ có nghĩa khi một thông báo vừa đến (đã bị bỏ trước khi thành dòng).
            var match = DuplicateMatcher.Match(subject, probes, DismissedWith(subject.Id, rows), settings, null, document.Transfers);
            if (match.Candidates.Count > 0)
            {
                result[subject.Id] = match;
            }
        }

        return result;
    }

    private static List<DuplicateProbe> Probes(IReadOnlyList<DuplicateCandidateRow> rows) => rows.Select(r => r.Probe).ToList();

    /// <summary>Cặp đã bị bác có thể lưu ở phía nào cũng được (bản mới hơn trỏ tới bản cũ hơn): xét cả hai chiều (BR-DUP-07).</summary>
    private static HashSet<Guid> DismissedWith(Guid subjectId, IReadOnlyList<DuplicateCandidateRow> rows)
    {
        var dismissed = new HashSet<Guid>();
        foreach (var row in rows.Where(r => r.DuplicateStatus == DuplicateStatus.Dismissed))
        {
            if (row.DuplicateOfTransactionId == subjectId)
            {
                dismissed.Add(row.Probe.Id);
            }
            else if (row.Probe.Id == subjectId && row.DuplicateOfTransactionId is { } other)
            {
                dismissed.Add(other);
            }
        }

        return dismissed;
    }
}
