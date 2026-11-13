using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Application.Transactions.Refunds;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// <c>POST /transactions/{id}/merge</c> (Phase 82, BR-DUP-09…14): gộp <paramref name="TransactionId"/> với <paramref name="WithTransactionId"/> thành một.
/// Bản giữ lại do <see cref="MergePolicy"/> chọn, không phụ thuộc thứ tự hai id. <paramref name="ClientRequestId"/> nhận để không vỡ hợp đồng
/// nhưng không dùng: gộp lại cùng cặp đã trả về survivor cũ (D13).
/// </summary>
public record MergeTransactionsCommand(Guid UserId, Guid TransactionId, Guid WithTransactionId, Guid? CategoryId = null, Guid? ClientRequestId = null);

public interface IMergeTransactionsCommandHandler
{
    Task<TransactionDto> HandleAsync(MergeTransactionsCommand command, CancellationToken ct = default);

    /// <summary><c>POST /transactions/{id}/merge/preview</c>: kết quả sẽ ra, không ghi gì. Cùng 404/422 với lệnh thật.</summary>
    Task<TransactionPreviewDto> PreviewAsync(MergeTransactionsCommand command, CancellationToken ct = default);
}

/// <summary>
/// Gộp dưới khoá hai dòng giao dịch (id tăng dần) → ví → kỳ ngân sách (BR-DUP-14). Đọc, tính, lưu trong cùng một DB transaction; việc bên ngoài
/// (cache, cảnh báo, tóm tắt ngày, dự báo, phản hồi AI) chạy sau khi commit, như Phase 81.
///
/// Bản bị gộp đi đúng đường hoàn tác của xoá (số dư trả lại ví) và bị xoá mềm kèm <c>merged_into_transaction_id</c>. Ngân sách cá nhân và nhóm
/// được TÍNH LẠI (không cộng delta), nên đổi ví, đổi ngày hay đổi tháng đều không lệch; cảnh báo chỉ khi số sau lớn hơn số trước (BR-DUP-11).
/// </summary>
public class MergeTransactionsCommandHandler : IMergeTransactionsCommandHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly IQrTransferRepository _qr;
    private readonly IReceiptScanRepository _receipts;
    private readonly IAnomalyRepository _anomalies;
    private readonly IRefundLedger _ledger;
    private readonly IAIServiceClient _ai;
    private readonly IDuplicateDetector _duplicates;
    private readonly RefundAfterSave _afterSave;
    private readonly IMergeSnapshotRepository? _snapshots;   // null: không ghi ảnh chụp (test cũ)
    private readonly ISystemConfigProvider? _config;         // null: giới hạn phí mặc định

    // Thay đổi số dư của lượt đang chạy, theo ví (để xem trước và ảnh chụp). Handler sống theo request; đặt lại đầu mỗi lượt (kể cả lượt thử lại).
    private readonly Dictionary<Guid, long> _deltas = new();

    public MergeTransactionsCommandHandler(
        ITransactionRepository transactions,
        IFinancialAccountRepository accounts,
        ICategoryRepository categories,
        IQrTransferRepository qr,
        IReceiptScanRepository receipts,
        IAnomalyRepository anomalies,
        IRefundLedger ledger,
        IAIServiceClient ai,
        IDuplicateDetector duplicates,
        IBudgetAlertNotifier budgetAlerts,
        ICacheService cache,
        IGroupLedger? groupLedger = null,
        IDailySummaryRefresher? summaries = null,
        IForecastAlertTrigger? forecastAlerts = null,
        IMergeSnapshotRepository? snapshots = null,
        ISystemConfigProvider? config = null)
    {
        _snapshots = snapshots;
        _config = config;
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _qr = qr;
        _receipts = receipts;
        _anomalies = anomalies;
        _ledger = ledger;
        _ai = ai;
        _duplicates = duplicates;
        _afterSave = new RefundAfterSave(budgetAlerts, cache, groupLedger, summaries, forecastAlerts);
    }

    private sealed record Outcome(Transaction Survivor, RefundEffects Effects, CategoryFeedback? Feedback, Transaction? Fee = null);

    private sealed record MovedReferences(List<Guid> QrIntents, List<Guid> DraftRefunds, List<Guid> DuplicateLinks);

    private sealed record CategoryFeedback(Guid TransactionId, string Package, string? PredictedSlug, string? CorrectedSlug);

    public async Task<TransactionDto> HandleAsync(MergeTransactionsCommand command, CancellationToken ct = default)
    {
        var outcome = await _transactions.RunWithTransactionsLockedAsync(
            command.UserId, [command.TransactionId, command.WithTransactionId], rows => MergeLockedAsync(command, rows, ct), ct);

        await _afterSave.RunAsync(command.UserId, outcome.Effects, ct);
        if (outcome.Feedback is { } feedback)
        {
            // IAIServiceClient tự nuốt lỗi: phản hồi không được làm hỏng một lượt gộp đã lưu.
            await _ai.SendFeedbackAsync(
                new FeedbackRequest(feedback.TransactionId, command.UserId, null, feedback.Package, feedback.PredictedSlug, feedback.CorrectedSlug, "category_correction"),
                ct);
        }

        var view = await _duplicates.ViewAsync(command.UserId, [outcome.Survivor], ct);
        return TransactionMapper.ToDto(outcome.Survivor, duplicates: view.GetValueOrDefault(outcome.Survivor.Id));
    }

    public async Task<TransactionPreviewDto> PreviewAsync(MergeTransactionsCommand command, CancellationToken ct = default)
    {
        var (outcome, deltas) = await _transactions.RunPreviewAsync(async () =>
        {
            var result = await _transactions.RunWithTransactionsLockedAsync(
                command.UserId, [command.TransactionId, command.WithTransactionId], rows => MergeLockedAsync(command, rows, ct), ct);
            return (result, new Dictionary<Guid, long>(_deltas));
        }, ct);

        return new TransactionPreviewDto(
            TransactionMapper.ToDto(outcome.Survivor),
            outcome.Fee is null ? null : TransactionMapper.ToDto(outcome.Fee),
            await BalancePreview.ChangesAsync(_accounts, command.UserId, deltas, ct));
    }

    private async Task<Outcome> MergeLockedAsync(MergeTransactionsCommand command, IReadOnlyDictionary<Guid, Transaction> rows, CancellationToken ct)
    {
        _deltas.Clear();
        var a = rows[command.TransactionId];
        var b = rows[command.WithTransactionId];
        if (AlreadyMerged(a, b) is { } done)
        {
            return new Outcome(done, RefundEffects.None, null);   // BR-DUP-14: gộp lại cùng cặp → survivor cũ, không làm gì thêm
        }

        EnsureLive(a);
        EnsureLive(b);

        var survivor = MergePolicy.ChooseSurvivor(a, b);
        var loser = ReferenceEquals(survivor, a) ? b : a;
        var qrA = await _qr.GetIntentByTransactionAsync(a.Id, command.UserId, ct);
        var qrB = await _qr.GetIntentByTransactionAsync(b.Id, command.UserId, ct);
        var loserRefunded = loser.Status == TransactionStatus.Confirmed
            && await _transactions.SumConfirmedRefundsAsync(command.UserId, loser.Id, null, ct) > 0;
        var transfers = _config is null ? new Admin.SystemConfig.TransferSettings() : (await _config.GetAsync(ct)).Document.Transfers;
        MergePolicy.EnsureMergeable(
            a, b, survivor, new MergeFacts(loserRefunded, qrA is not null, qrB is not null, transfers.MaxFeeCents, transfers.MaxFeePercent));
        if (survivor.TransactionType == TransactionType.Transfer)
        {
            var (transferQr, otherQr) = ReferenceEquals(survivor, a) ? (qrA, qrB) : (qrB, qrA);
            return await MergeIntoTransferAsync(command.UserId, survivor, loser, otherQr, transferQr, ct);
        }

        var chosen = await ChosenCategoryAsync(command, ct);
        var before = new[] { survivor, loser }.Where(t => t.Status == TransactionStatus.Confirmed).Select(RefundScopeSnapshot.Of).ToList();
        var feedback = await FeedbackBaseAsync(command.UserId, survivor, loser, ct);
        var fields = MergePolicy.Fields(survivor, loser, chosen?.Id);
        var now = DateTimeOffset.UtcNow;
        var (survivorQr, loserQr) = ReferenceEquals(survivor, a) ? (qrA, qrB) : (qrB, qrA);

        var rowsBefore = new[] { survivor, loser }.Select(SnapshotRow.Of).ToList();
        var receiptMoves = await _receipts.GetLinkedScanIdAsync(loser.Id, command.UserId, ct) is not null
            && await _receipts.GetLinkedScanIdAsync(survivor.Id, command.UserId, ct) is null;

        await MoveMoneyAsync(command.UserId, survivor, loser, fields.AccountId, now, ct);
        var moved = await ApplyFieldsAsync(survivor, loser, fields, chosen, now, ct);
        var anomalies = await AnomaliesToRemoveAsync(command.UserId, survivor, loser, moved, ct);
        var references = await MoveReferencesAsync(command.UserId, survivor, loser, loserQr, survivorQr, moved, now, ct);

        loser.DeletedAt = now;
        loser.MergedIntoTransactionId = survivor.Id;
        loser.DuplicateOfTransactionId = null;
        loser.DuplicateStatus = null;
        loser.UpdatedAt = now;

        var effects = await _ledger.RecomputeAsync(command.UserId, [survivor, loser], before, ct);
        _snapshots?.Add(MergeSnapshotFactory.Create(
            command.UserId, survivor.Id, loser.Id, MergeSnapshotKind.DuplicateMerge, null,
            new SnapshotBefore(
                rowsBefore, new Dictionary<Guid, long>(_deltas), null, references.QrIntents, references.DraftRefunds, references.DuplicateLinks,
                anomalies, receiptMoves),
            now));
        await SaveAsync(command.UserId, survivor, loser, ct);

        var corrected = survivor.Category?.Slug;
        return new Outcome(
            survivor,
            effects,
            feedback is not null && feedback.PredictedSlug != corrected ? feedback with { TransactionId = survivor.Id, CorrectedSlug = corrected } : null);
    }

    /// <summary>
    /// Phase 83 (BR-TRF-06, D8, D9): thông báo của ngân hàng về một Transfer người dùng đã ghi tay. Transfer giữ nguyên ví, số tiền, không danh mục;
    /// chỉ nhận giờ và số dư ngân hàng báo từ chiều tiền ra (chiều tiền vào chỉ đặt giờ khi chưa có thông báo nào gộp vào). Thông báo đã xác nhận thì
    /// tiền của nó được trả lại ví; tiền ra lớn hơn số chuyển thì phần chênh thành khoản phí, nên ví nguồn kết thúc đúng như ngân hàng báo.
    /// </summary>
    private async Task<Outcome> MergeIntoTransferAsync(
        Guid userId, Transaction transfer, Transaction notice, QrTransferIntent? noticeQr, QrTransferIntent? transferQr, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var before = new[] { transfer, notice }.Where(t => t.Status == TransactionStatus.Confirmed).Select(RefundScopeSnapshot.Of).ToList();
        var rowsBefore = new[] { transfer, notice }.Select(SnapshotRow.Of).ToList();
        var receiptMoves = await _receipts.GetLinkedScanIdAsync(notice.Id, userId, ct) is not null
            && await _receipts.GetLinkedScanIdAsync(transfer.Id, userId, ct) is null;

        if (notice.Status == TransactionStatus.Confirmed)
        {
            await AdjustAsync(userId, notice.FinancialAccountId, -Signed(notice), now, ct);
        }

        Transaction? fee = null;
        if (notice.TransactionType == TransactionType.Debit)
        {
            var feeCents = notice.AmountCents - transfer.AmountCents;
            if (feeCents > 0)
            {
                fee = TransferFee.Build(
                    userId, transfer.Id, notice, transfer.FinancialAccountId, feeCents, await _categories.GetSystemBySlugAsync(TransferFee.CategorySlug, ct), now);
                await AdjustAsync(userId, transfer.FinancialAccountId, -feeCents, now, ct);
            }

            transfer.TransactedAt = notice.TransactedAt;
            transfer.BalanceAfterCents = fee is null ? notice.BalanceAfterCents : null;
        }
        else if (transfer.MergedSources?.Contains("notification") != true)
        {
            transfer.TransactedAt = notice.TransactedAt;
        }

        transfer.MergedSources = MergePolicy.MergedSources(transfer, notice);
        if (transfer.DuplicateOfTransactionId == notice.Id)
        {
            transfer.DuplicateOfTransactionId = null;
            transfer.DuplicateStatus = null;
        }

        transfer.UpdatedAt = now;
        var anomalies = await AnomaliesToRemoveAsync(userId, transfer, notice, survivorMoved: false, ct);
        var references = await MoveReferencesAsync(userId, transfer, notice, noticeQr, transferQr, survivorMoved: false, now, ct);
        notice.DeletedAt = now;
        notice.MergedIntoTransactionId = transfer.Id;
        notice.DuplicateOfTransactionId = null;
        notice.DuplicateStatus = null;
        notice.UpdatedAt = now;

        var pending = fee is null ? new List<Transaction> { transfer, notice } : [transfer, notice, fee];
        var effects = await _ledger.RecomputeAsync(userId, pending, before, ct);
        _snapshots?.Add(MergeSnapshotFactory.Create(
            userId, transfer.Id, notice.Id, MergeSnapshotKind.DuplicateMerge, null,
            new SnapshotBefore(
                rowsBefore, new Dictionary<Guid, long>(_deltas), fee?.Id, references.QrIntents, references.DraftRefunds, references.DuplicateLinks,
                anomalies, receiptMoves),
            now));
        if (fee is not null)
        {
            await _transactions.AddAsync(fee, ct);
        }

        await SaveAsync(userId, transfer, notice, ct);
        return new Outcome(transfer, effects, null, fee);
    }

    /// <summary>Cặp này đã gộp xong (một bản trỏ <c>merged_into</c> tới bản kia): trả survivor nếu nó còn sống.</summary>
    private static Transaction? AlreadyMerged(Transaction a, Transaction b)
        => a.MergedIntoTransactionId == b.Id && b.DeletedAt is null ? b
            : b.MergedIntoTransactionId == a.Id && a.DeletedAt is null ? a
            : null;

    private static void EnsureLive(Transaction t)
    {
        if (t.MergedIntoTransactionId is not null)
        {
            throw new BusinessRuleException(TransactionErrorCodes.MergeNotAllowed, "Một trong hai khoản đã được gộp vào giao dịch khác.");
        }

        if (t.DeletedAt is not null)
        {
            throw new NotFoundException("Transaction", t.Id);
        }
    }

    private async Task<Category?> ChosenCategoryAsync(MergeTransactionsCommand command, CancellationToken ct)
    {
        if (command.CategoryId is not { } id)
        {
            return null;
        }

        var category = await _categories.GetByIdAsync(id, ct);
        return category is not null && (category.UserId is null || category.UserId == command.UserId)
            ? category
            : throw new NotFoundException("Category", id);
    }

    /// <summary>Danh mục AI đoán cho phần thông báo, để gửi <c>category_correction</c> nếu danh mục cuối khác (D16).</summary>
    private async Task<CategoryFeedback?> FeedbackBaseAsync(Guid userId, Transaction survivor, Transaction loser, CancellationToken ct)
    {
        var bank = survivor.Source == TransactionSource.Notification ? survivor : loser.Source == TransactionSource.Notification ? loser : null;
        if (bank is null)
        {
            return null;
        }

        var account = await _accounts.GetByIdAsync(bank.FinancialAccountId, userId, ct);
        return new CategoryFeedback(bank.Id, account?.PackageName ?? "unknown", bank.Category?.Slug, null);
    }

    /// <summary>
    /// Số dư: bản bị gộp đã xác nhận thì trả lại ví của nó (đường hoàn tác của xoá); survivor đã xác nhận thì rút khỏi ví cũ rồi áp vào ví mới
    /// (chuyển số dư nếu đổi ví); survivor còn là nháp thì được xác nhận và áp vào ví mới lần đầu.
    /// </summary>
    private async Task MoveMoneyAsync(Guid userId, Transaction survivor, Transaction loser, Guid targetAccountId, DateTimeOffset now, CancellationToken ct)
    {
        if (loser.Status == TransactionStatus.Confirmed)
        {
            await AdjustAsync(userId, loser.FinancialAccountId, -Signed(loser), now, ct);
        }

        if (survivor.Status == TransactionStatus.Confirmed)
        {
            await AdjustAsync(userId, survivor.FinancialAccountId, -Signed(survivor), now, ct);
        }

        var target = await AdjustAsync(userId, targetAccountId, Signed(survivor), now, ct);
        survivor.FinancialAccount = target;
    }

    private static long Signed(Transaction t) => t.TransactionType == TransactionType.Credit ? t.AmountCents : -t.AmountCents;

    private async Task<FinancialAccount> AdjustAsync(Guid userId, Guid accountId, long delta, DateTimeOffset now, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
        _deltas[accountId] = _deltas.GetValueOrDefault(accountId) + delta;
        account.BalanceCents += delta;
        account.UpdatedAt = now;
        return account;
    }

    /// <summary>Áp BR-DUP-10 lên survivor và xác nhận nó. Trả true khi ví hoặc ngày đổi (đánh dấu bất thường đang mở phải được xét lại).</summary>
    private async Task<bool> ApplyFieldsAsync(
        Transaction survivor, Transaction loser, MergeFields fields, Category? chosen, DateTimeOffset now, CancellationToken ct)
    {
        var moved = survivor.FinancialAccountId != fields.AccountId || survivor.TransactedAt != fields.TransactedAt;
        survivor.FinancialAccountId = fields.AccountId;
        survivor.TransactedAt = fields.TransactedAt;
        survivor.BalanceAfterCents = fields.BalanceAfterCents;
        survivor.Description = fields.Description;
        survivor.MerchantName = fields.MerchantName;
        if (survivor.CategoryId != fields.CategoryId)
        {
            survivor.CategoryId = fields.CategoryId;
            survivor.Category = chosen ?? (fields.CategoryId is { } id ? await _categories.GetByIdAsync(id, ct) : null);
        }

        survivor.Status = TransactionStatus.Confirmed;
        survivor.MergedSources = MergePolicy.MergedSources(survivor, loser);
        if (survivor.DuplicateOfTransactionId == loser.Id)
        {
            survivor.DuplicateOfTransactionId = null;
            survivor.DuplicateStatus = null;
        }

        survivor.UpdatedAt = now;
        return moved;
    }

    /// <summary>Id các đánh dấu bất thường sắp bị bỏ (D10), ghi vào ảnh chụp trước khi bỏ.</summary>
    private async Task<List<Guid>> AnomaliesToRemoveAsync(Guid userId, Transaction survivor, Transaction loser, bool survivorMoved, CancellationToken ct)
        => (await _anomalies.GetForTransactionsAsync(userId, [survivor.Id, loser.Id], ct))
            .Where(a => a.TransactionId == loser.Id || (survivorMoved && a.Resolution is null))
            .Select(a => a.Id)
            .ToList();

    /// <summary>Chuyển mọi thứ đang trỏ tới bản bị gộp sang survivor (bảng tham chiếu trong kế hoạch Phase 82; D6, D7, D9, D10).</summary>
    private async Task<MovedReferences> MoveReferencesAsync(
        Guid userId, Transaction survivor, Transaction loser, QrTransferIntent? loserQr, QrTransferIntent? survivorQr, bool survivorMoved, DateTimeOffset now, CancellationToken ct)
    {
        var moved = new MovedReferences([], [], []);

        // D6: ý định QR khớp với bản bị gộp đi theo survivor; danh mục người dùng chọn lúc quét chỉ dùng khi survivor chưa có danh mục.
        if (loserQr is not null && survivorQr is null)
        {
            moved.QrIntents.Add(loserQr.Id);
            loserQr.MatchedTransactionId = survivor.Id;
            if (survivor.CategoryId is null && survivor.TransactionType != TransactionType.Transfer && loserQr.CategoryId is { } qrCategory)
            {
                survivor.CategoryId = qrCategory;
                survivor.Category = await _categories.GetByIdAsync(qrCategory, ct);
            }
        }

        // D7: nháp hoàn/đảo (Phase 81) đang chờ liên kết với bản bị gộp → liên kết với survivor. Hoàn đã xác nhận thì đã bị từ chối ở trên.
        foreach (var draft in await _transactions.ListRelatedAsync(userId, loser.Id, ct))
        {
            draft.RelatedTransactionId = survivor.Id;
            draft.UpdatedAt = now;
            moved.DraftRefunds.Add(draft.Id);
        }

        // D9: các bản khác đang nghi trùng / đã bác với bản bị gộp → trỏ sang survivor; thành tự trỏ thì bỏ liên kết.
        foreach (var linked in await _transactions.ListDuplicateLinksToAsync(userId, loser.Id, ct))
        {
            if (linked.Id == survivor.Id)
            {
                linked.DuplicateOfTransactionId = null;
                linked.DuplicateStatus = null;
            }
            else
            {
                linked.DuplicateOfTransactionId = survivor.Id;
            }

            linked.UpdatedAt = now;
            moved.DuplicateLinks.Add(linked.Id);
        }

        // D10: bỏ đánh dấu bất thường của bản bị gộp; survivor đổi ví/ngày thì bỏ đánh dấu đang mở để lượt quét sau xét lại.
        await _anomalies.RemoveForTransactionAsync(userId, loser.Id, openOnly: false, ct);
        if (survivorMoved)
        {
            await _anomalies.RemoveForTransactionAsync(userId, survivor.Id, openOnly: true, ct);
        }

        return moved;
    }

    /// <summary>
    /// Lưu trong DB transaction đang mở. <c>notification_log_id</c> là duy nhất nên khi chuyển nó sang survivor phải gỡ khỏi bản bị gộp và lưu trước
    /// (D8); liên kết hoá đơn chuyển bằng một câu SQL sau đó.
    /// </summary>
    private async Task SaveAsync(Guid userId, Transaction survivor, Transaction loser, CancellationToken ct)
    {
        if (survivor.NotificationLogId is null && loser.NotificationLogId is { } log)
        {
            loser.NotificationLogId = null;
            await _transactions.UpdateAsync(loser, ct);
            survivor.NotificationLogId = log;
        }

        await _transactions.UpdateAsync(survivor, ct);
        await _receipts.MoveLinkAsync(userId, loser.Id, survivor.Id, ct);
    }
}
