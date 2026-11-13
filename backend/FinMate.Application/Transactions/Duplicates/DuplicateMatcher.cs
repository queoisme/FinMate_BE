using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Duplicates;

/// <summary>
/// Ảnh chụp một giao dịch đủ để so trùng. <see cref="PackageName"/> là package của thông báo đã sinh ra giao dịch (null nếu không có);
/// <see cref="MergedSources"/> cho biết một survivor nhập tay đã được ngân hàng xác thực hay chưa.
/// </summary>
public sealed record DuplicateProbe(
    Guid Id,
    TransactionSource Source,
    TransactionType Type,
    long AmountCents,
    DateTimeOffset TransactedAt,
    DateTimeOffset CreatedAt,
    Guid AccountId,
    string? MerchantName,
    long? BalanceAfterCents,
    string? PackageName,
    TransactionRelation? Relation,
    bool IsDeleted,
    bool IsMerged,
    string[]? MergedSources,
    Guid? NotificationLogId);

/// <summary>Cặp nguồn của hai giao dịch được so. Mỗi cặp có cửa sổ thời gian riêng (<see cref="DuplicateSettings"/>).</summary>
public enum DuplicatePair
{
    NotificationNotification,
    ManualNotification,
    ReceiptNotification,

    /// <summary>Hoá đơn ↔ nhập tay: chỉ cảnh báo, không gắn nghi trùng (D4).</summary>
    ReceiptManual,
}

public sealed record RankedDuplicate(Guid Id, DuplicatePair Pair)
{
    /// <summary>Có gắn <c>duplicate_status = suspected</c> lên giao dịch đang xét hay chỉ cảnh báo.</summary>
    public bool MarksSuspected => Pair != DuplicatePair.ReceiptManual;
}

/// <param name="SameEventId">Có giá trị khi giao dịch đang xét là chính sự kiện ngân hàng này gửi lại (BR-DUP-05): bỏ âm thầm.</param>
/// <param name="Candidates">Tối đa <see cref="DuplicateMatcher.MaxCandidates"/> ứng viên, hạng 1 trước.</param>
public sealed record DuplicateMatch(Guid? SameEventId, IReadOnlyList<RankedDuplicate> Candidates)
{
    public static readonly DuplicateMatch None = new(null, []);

    /// <summary>Ứng viên hạng 1 dùng để gắn nghi trùng, nếu có.</summary>
    public RankedDuplicate? SuspectedOf => Candidates.FirstOrDefault(c => c.MarksSuspected);
}

/// <summary>
/// Nơi duy nhất quyết định một giao dịch có thể trùng với giao dịch nào (Phase 82, BR-DUP-02). Hàm thuần: repository nạp ứng viên thô
/// (cùng user, cùng chiều, cùng số tiền, trong cửa sổ lớn nhất), hàm này áp phần còn lại của BR-DUP-03, BR-DUP-04 và BR-DUP-05.
/// Cờ trùng của AI Service chỉ là một tín hiệu xếp hạng (<c>aiHintLogId</c>), không bao giờ tự tạo ra ứng viên.
/// </summary>
public static class DuplicateMatcher
{
    public const int MaxCandidates = 3;

    private const string NotificationSource = "notification";

    public static DuplicateMatch Match(
        DuplicateProbe subject,
        IReadOnlyCollection<DuplicateProbe> candidates,
        IReadOnlySet<Guid> dismissed,
        DuplicateSettings settings,
        Guid? aiHintLogId = null)
    {
        if (!CanHaveDuplicates(subject))
        {
            return DuplicateMatch.None;
        }

        var subjectKey = CounterpartyKey.From(subject.MerchantName);
        var ranked = new List<(DuplicateProbe Probe, DuplicatePair Pair)>();
        foreach (var other in candidates)
        {
            if (!IsComparable(subject, other, dismissed) || PairOf(subject.Source, other.Source) is not { } pair)
            {
                continue;
            }

            var apart = (subject.TransactedAt - other.TransactedAt).Duration();
            switch (CompareBalances(subject, other, apart, settings))
            {
                case BalanceVerdict.SameEvent:
                    return new DuplicateMatch(other.Id, []);
                case BalanceVerdict.DifferentEvents:
                    continue;
            }

            if (apart <= WindowOf(pair, settings))
            {
                ranked.Add((other, pair));
            }
        }

        var top = ranked
            .OrderByDescending(r => r.Probe.AccountId == subject.AccountId)
            .ThenByDescending(r => subjectKey is not null && CounterpartyKey.From(r.Probe.MerchantName) == subjectKey)
            .ThenByDescending(r => aiHintLogId is not null && r.Probe.NotificationLogId == aiHintLogId)
            .ThenBy(r => (subject.TransactedAt - r.Probe.TransactedAt).Duration())
            .ThenBy(r => r.Probe.CreatedAt)
            .ThenBy(r => r.Probe.Id)
            .Take(MaxCandidates)
            .Select(r => new RankedDuplicate(r.Probe.Id, r.Pair))
            .ToList();
        return new DuplicateMatch(null, top);
    }

    /// <summary>Cửa sổ rộng nhất trên mọi cặp: repository chỉ cần nạp ứng viên trong khoảng này.</summary>
    public static TimeSpan WidestWindow(DuplicateSettings settings)
        => new[]
        {
            TimeSpan.FromMinutes(settings.NotificationWindowMinutes),
            TimeSpan.FromHours(settings.ManualWindowHours),
            TimeSpan.FromHours(settings.ReceiptWindowHours),
        }.Max();

    /// <summary>Chuyển khoản nội bộ và khoản hoàn/đảo (Phase 81) không bao giờ là bản trùng, ở cả hai phía.</summary>
    public static bool CanHaveDuplicates(DuplicateProbe probe)
        => probe.Type is TransactionType.Debit or TransactionType.Credit && probe.Relation is null && !probe.IsDeleted && !probe.IsMerged;

    private static bool IsComparable(DuplicateProbe subject, DuplicateProbe other, IReadOnlySet<Guid> dismissed)
        => other.Id != subject.Id
           && CanHaveDuplicates(other)
           && other.Type == subject.Type
           && other.AmountCents == subject.AmountCents
           && !dismissed.Contains(other.Id);

    private static DuplicatePair? PairOf(TransactionSource a, TransactionSource b)
        => (Kind(a), Kind(b)) switch
        {
            (SourceKind.Bank, SourceKind.Bank) => DuplicatePair.NotificationNotification,
            (SourceKind.Bank, SourceKind.Typed) or (SourceKind.Typed, SourceKind.Bank) => DuplicatePair.ManualNotification,
            (SourceKind.Bank, SourceKind.Receipt) or (SourceKind.Receipt, SourceKind.Bank) => DuplicatePair.ReceiptNotification,
            (SourceKind.Receipt, SourceKind.Typed) or (SourceKind.Typed, SourceKind.Receipt) => DuplicatePair.ReceiptManual,
            _ => null,   // hai khoản đều do người dùng nhập: ý của họ, không so
        };

    private static TimeSpan WindowOf(DuplicatePair pair, DuplicateSettings settings) => pair switch
    {
        DuplicatePair.NotificationNotification => TimeSpan.FromMinutes(settings.NotificationWindowMinutes),
        DuplicatePair.ManualNotification => TimeSpan.FromHours(settings.ManualWindowHours),
        _ => TimeSpan.FromHours(settings.ReceiptWindowHours),
    };

    /// <summary>
    /// BR-DUP-05 (D5): hai giao dịch cùng được ngân hàng báo, cùng provider, đều có số dư sau giao dịch. Số dư khác → hai sự kiện thật
    /// (bất kể cách nhau bao lâu). Số dư bằng → cùng một sự kiện, nhưng chỉ khi trong cửa sổ thông báo, vì số dư có thể lặp lại thật.
    /// </summary>
    private static BalanceVerdict CompareBalances(DuplicateProbe subject, DuplicateProbe other, TimeSpan apart, DuplicateSettings settings)
    {
        if (!IsBankVerified(subject) || !IsBankVerified(other)
            || subject.PackageName is null || subject.PackageName != other.PackageName
            || subject.BalanceAfterCents is not { } a || other.BalanceAfterCents is not { } b)
        {
            return BalanceVerdict.Unknown;
        }

        if (a != b)
        {
            return BalanceVerdict.DifferentEvents;
        }

        return apart <= TimeSpan.FromMinutes(settings.NotificationWindowMinutes) ? BalanceVerdict.SameEvent : BalanceVerdict.Unknown;
    }

    /// <summary>Số dư chỉ là số của ngân hàng trên dòng đến từ thông báo, hoặc survivor đã gộp một thông báo.</summary>
    private static bool IsBankVerified(DuplicateProbe probe)
        => probe.Source == TransactionSource.Notification || probe.MergedSources?.Contains(NotificationSource) == true;

    private static SourceKind Kind(TransactionSource source) => source switch
    {
        TransactionSource.Notification => SourceKind.Bank,
        TransactionSource.Receipt => SourceKind.Receipt,
        _ => SourceKind.Typed,   // Manual, Voice, Support
    };

    private enum SourceKind
    {
        Bank,
        Typed,
        Receipt,
    }

    private enum BalanceVerdict
    {
        Unknown,
        SameEvent,
        DifferentEvents,
    }
}
