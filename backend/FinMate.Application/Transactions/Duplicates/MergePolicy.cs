using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Duplicates;

/// <summary>Sự thật về hai bản cần đọc từ DB trước khi quyết gộp. <c>A</c>/<c>B</c> theo đúng thứ tự đưa vào <see cref="MergePolicy.EnsureMergeable"/>.</summary>
/// <param name="LoserHasConfirmedRefunds">Bản bị gộp đang có khoản hoàn/đảo đã xác nhận (Phase 81).</param>
/// <param name="AMatchedQr">Bản thứ nhất đã khớp một ý định QR.</param>
/// <param name="BMatchedQr">Bản thứ hai đã khớp một ý định QR.</param>
/// <param name="MaxFeeCents">Phase 83: giới hạn phí khi gộp một thông báo tiền ra vào một Transfer đã ghi tay (<c>transfers.maxFeeCents</c>).</param>
/// <param name="MaxFeePercent">Như trên, theo % số tiền chuyển.</param>
public sealed record MergeFacts(bool LoserHasConfirmedRefunds, bool AMatchedQr, bool BMatchedQr, long MaxFeeCents = 20_000, int MaxFeePercent = 2);

/// <summary>Giá trị cuối của survivor sau gộp (BR-DUP-10). Số tiền không có ở đây: hai bản luôn bằng nhau.</summary>
public sealed record MergeFields(
    Guid AccountId, DateTimeOffset TransactedAt, long? BalanceAfterCents, Guid? CategoryId, string? Description, string? MerchantName);

/// <summary>
/// Luật gộp hai giao dịch là một (Phase 82). Hàm thuần: handler khoá hai dòng, đọc <see cref="MergeFacts"/>, rồi hỏi ở đây bản nào giữ lại, trường
/// nào lấy từ đâu và có được gộp không.
/// </summary>
public static class MergePolicy
{
    /// <summary>
    /// BR-DUP-09. Đúng một bản đã xác nhận → giữ bản đó (không phải chuyển tham chiếu nhóm, hoàn tiền, QR, bất thường). Cả hai đã xác nhận → bản
    /// tạo trước. Cả hai là nháp → bản từ thông báo (rồi bản tạo trước). Survivor luôn được xác nhận sau gộp.
    /// </summary>
    public static Transaction ChooseSurvivor(Transaction a, Transaction b)
    {
        // Phase 83 (D9): một Transfer người dùng đã ghi tay luôn được giữ; thông báo của ngân hàng chỉ góp giờ và số dư vào nó.
        if (a.TransactionType == TransactionType.Transfer || b.TransactionType == TransactionType.Transfer)
        {
            return a.TransactionType == TransactionType.Transfer ? a : b;
        }

        var aConfirmed = a.Status == TransactionStatus.Confirmed;
        var bConfirmed = b.Status == TransactionStatus.Confirmed;
        if (aConfirmed != bConfirmed)
        {
            return aConfirmed ? a : b;
        }

        if (!aConfirmed)
        {
            var aBank = a.Source == TransactionSource.Notification;
            var bBank = b.Source == TransactionSource.Notification;
            if (aBank != bBank)
            {
                return aBank ? a : b;
            }
        }

        return Earlier(a, b);
    }

    /// <summary>BR-DUP-12, D6, D12. Từ chối bằng 422 <c>MERGE_NOT_ALLOWED</c>; lý do nằm trong message. Không đòi cửa sổ thời gian hay đang nghi trùng.</summary>
    public static void EnsureMergeable(Transaction a, Transaction b, Transaction survivor, MergeFacts facts)
    {
        if (a.Id == b.Id)
        {
            throw Refuse("Không thể gộp một giao dịch với chính nó.");
        }

        if (a.TransactionType == TransactionType.Transfer || b.TransactionType == TransactionType.Transfer)
        {
            EnsureFitsTheTransfer(a.TransactionType == TransactionType.Transfer ? a : b, a.TransactionType == TransactionType.Transfer ? b : a, facts);
        }
        else if (a.TransactionType != b.TransactionType)
        {
            throw Refuse("Chỉ gộp được hai khoản cùng là chi hoặc cùng là thu.");
        }

        if (a.AmountCents != b.AmountCents && a.TransactionType != TransactionType.Transfer && b.TransactionType != TransactionType.Transfer)
        {
            throw Refuse("Hai khoản khác số tiền nên không phải cùng một giao dịch.");
        }

        if (a.Relation is not null || b.Relation is not null)
        {
            throw Refuse("Khoản hoàn tiền hoặc đảo giao dịch không gộp được.");
        }

        var loser = ReferenceEquals(survivor, a) ? b : a;
        if (loser.Status == TransactionStatus.Confirmed && (facts.LoserHasConfirmedRefunds || loser.IsReversed))
        {
            throw Refuse("Khoản sẽ bị gộp đã có khoản hoàn hoặc bị đảo. Xoá các khoản đó trước rồi gộp.");
        }

        if (loser.GroupId is not null)
        {
            throw Refuse("Khoản sẽ bị gộp đã chia sẻ vào nhóm. Bỏ chia sẻ trước rồi gộp.");
        }

        if (facts.AMatchedQr && facts.BMatchedQr)
        {
            throw Refuse("Cả hai khoản đều đã khớp một lần quét QR nên là hai lần chuyển khác nhau.");
        }
    }

    /// <summary>
    /// BR-TRF-06 (D8): chỉ gộp được tiền ra ở ví nguồn (bằng số Transfer hoặc lớn hơn trong giới hạn phí) hoặc tiền vào ở ví đích (bằng đúng).
    /// </summary>
    private static void EnsureFitsTheTransfer(Transaction transfer, Transaction other, MergeFacts facts)
    {
        if (other.TransactionType == TransactionType.Transfer)
        {
            throw Refuse("Không gộp hai chuyển khoản với nhau.");
        }

        if (other.TransactionType == TransactionType.Credit)
        {
            if (other.FinancialAccountId != transfer.CounterAccountId || other.AmountCents != transfer.AmountCents)
            {
                throw Refuse("Khoản tiền vào phải ở ví đích của chuyển khoản và bằng đúng số tiền.");
            }

            return;
        }

        var fee = other.AmountCents - transfer.AmountCents;
        if (other.FinancialAccountId != transfer.FinancialAccountId
            || fee < 0 || fee > facts.MaxFeeCents || fee * 100 > transfer.AmountCents * facts.MaxFeePercent)
        {
            throw Refuse("Khoản tiền ra phải ở ví nguồn của chuyển khoản, bằng số tiền chuyển hoặc chỉ chênh phí.");
        }
    }

    /// <summary>
    /// BR-DUP-10. Số tiền, thời điểm, ví (và số dư ngân hàng báo) lấy từ bản thông báo, không có thì từ hoá đơn. Danh mục, ghi chú, tên nơi chi
    /// lấy từ bản người dùng nhập; thiếu thì lấy từ bản còn lại. Danh mục trong request thắng tất cả.
    /// </summary>
    public static MergeFields Fields(Transaction survivor, Transaction loser, Guid? overrideCategoryId)
    {
        var bank = BankSide(survivor, loser);
        var user = UserSide(survivor, loser);
        var other = ReferenceEquals(user, survivor) ? loser : survivor;

        return new MergeFields(
            bank?.FinancialAccountId ?? survivor.FinancialAccountId,
            bank?.TransactedAt ?? survivor.TransactedAt,
            bank?.Source == TransactionSource.Notification ? bank.BalanceAfterCents : survivor.BalanceAfterCents,
            overrideCategoryId ?? user.CategoryId ?? other.CategoryId,
            user.Description ?? other.Description,
            user.MerchantName ?? other.MerchantName);
    }

    /// <summary>Các kênh đã góp vào survivor, gộp cả các lần gộp trước, chữ thường, sắp xếp để so được.</summary>
    public static string[] MergedSources(Transaction survivor, Transaction loser)
        => SourcesOf(survivor).Concat(SourcesOf(loser)).Distinct().Order(StringComparer.Ordinal).ToArray();

    /// <summary>Bản mang dữ liệu ngân hàng: thông báo trước, rồi hoá đơn; hai bản cùng loại thì ưu tiên survivor.</summary>
    public static Transaction? BankSide(Transaction survivor, Transaction loser)
        => Pick(survivor, loser, t => t.Source == TransactionSource.Notification)
           ?? Pick(survivor, loser, t => t.Source == TransactionSource.Receipt);

    private static Transaction UserSide(Transaction survivor, Transaction loser)
        => Pick(survivor, loser, t => t.Source is TransactionSource.Manual or TransactionSource.Voice or TransactionSource.Support)
           ?? Pick(survivor, loser, t => t.Source == TransactionSource.Receipt)
           ?? survivor;

    private static Transaction? Pick(Transaction survivor, Transaction loser, Func<Transaction, bool> match)
        => match(survivor) ? survivor : match(loser) ? loser : null;

    private static IEnumerable<string> SourcesOf(Transaction t)
        => t.MergedSources is { Length: > 0 } merged ? merged : [t.Source.ToString().ToLowerInvariant()];

    private static Transaction Earlier(Transaction a, Transaction b)
        => a.CreatedAt != b.CreatedAt
            ? (a.CreatedAt < b.CreatedAt ? a : b)
            : (a.Id.CompareTo(b.Id) <= 0 ? a : b);

    private static BusinessRuleException Refuse(string reason) => new(TransactionErrorCodes.MergeNotAllowed, reason);
}
