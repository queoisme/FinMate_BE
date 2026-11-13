using FinMate.Application.Common.Interfaces;
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

    /// <summary>Ứng viên tính khi đọc cho nhiều giao dịch của cùng user (một truy vấn). Khoá = id giao dịch; không có ứng viên thì không có khoá.</summary>
    Task<IReadOnlyDictionary<Guid, DuplicateMatch>> FindForManyAsync(Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default);
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

        var settings = (await _config.GetAsync(ct)).Document.Duplicates;
        var window = DuplicateMatcher.WidestWindow(settings);
        var rows = await _transactions.ListDuplicateCandidatesAsync(
            new DuplicateCandidateQuery(transaction.UserId, [transaction.AmountCents], subject.TransactedAt - window, subject.TransactedAt + window), ct);

        var match = DuplicateMatcher.Match(subject, Probes(rows), DismissedWith(subject.Id, rows), settings, aiHintLogId);
        if (match.SameEventId is null && match.SuspectedOf is { } top)
        {
            transaction.DuplicateOfTransactionId = top.Id;
            transaction.DuplicateStatus = DuplicateStatus.Suspected;
        }

        return match;
    }

    public async Task<IReadOnlyDictionary<Guid, DuplicateMatch>> FindForManyAsync(
        Guid userId, IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var subjects = transactions.Select(t => ProbeOf(t, null)).Where(DuplicateMatcher.CanHaveDuplicates).ToList();
        if (subjects.Count == 0)
        {
            return new Dictionary<Guid, DuplicateMatch>();
        }

        var settings = (await _config.GetAsync(ct)).Document.Duplicates;
        var window = DuplicateMatcher.WidestWindow(settings);
        var rows = await _transactions.ListDuplicateCandidatesAsync(
            new DuplicateCandidateQuery(
                userId,
                subjects.Select(s => s.AmountCents).Distinct().ToList(),
                subjects.Min(s => s.TransactedAt) - window,
                subjects.Max(s => s.TransactedAt) + window),
            ct);
        var probes = Probes(rows);

        var result = new Dictionary<Guid, DuplicateMatch>();
        foreach (var subject in subjects)
        {
            // Lúc đọc chỉ cần danh sách ứng viên; "cùng sự kiện" chỉ có nghĩa khi một thông báo vừa đến (đã bị bỏ trước khi thành dòng).
            var match = DuplicateMatcher.Match(subject, probes, DismissedWith(subject.Id, rows), settings);
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
