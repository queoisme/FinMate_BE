using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Transactions.Refunds;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// <c>POST /transactions/{id}/convert-to-transfer</c> (Phase 83). <paramref name="TransactionId"/> là khoản chi → ví của nó là nguồn,
/// <paramref name="CounterAccountId"/> là đích; là khoản thu → ngược lại. Có <paramref name="PairedTransactionId"/> → ghép hai chiều (BR-TRF-08),
/// khi đó <paramref name="CounterAccountId"/> phải là ví của chiều kia.
/// </summary>
public record ConvertToTransferCommand(
    Guid UserId, Guid TransactionId, Guid CounterAccountId, Guid? PairedTransactionId = null, Guid? ClientRequestId = null);

/// <param name="Warnings">Mã cảnh báo không chặn (S6), ví dụ <c>CASH_BALANCE_NEGATIVE</c>; null khi không có.</param>
public record TransferConversionDto(TransactionDto Transfer, TransactionDto? Fee, IReadOnlyList<string>? Warnings = null);

public interface IConvertToTransferCommandHandler
{
    Task<TransferConversionDto> HandleAsync(ConvertToTransferCommand command, CancellationToken ct = default);

    /// <summary><c>POST /transactions/{id}/convert-to-transfer/preview</c>: kết quả sẽ ra, không ghi gì. Cùng 404/422 với lệnh thật.</summary>
    Task<TransactionPreviewDto> PreviewAsync(ConvertToTransferCommand command, CancellationToken ct = default);
}

public static class TransferWarnings
{
    /// <summary>Ví Tiền mặt bị âm sau chuyển đổi (vd. nộp ATM mà ví Tiền mặt chưa ghi đủ). Vẫn cho phép: hệ thống không chặn số dư âm.</summary>
    public const string CashBalanceNegative = "CASH_BALANCE_NEGATIVE";
}

/// <summary>
/// Đổi một khoản chi/thu (hoặc một cặp) thành Transfer dưới khoá các dòng giao dịch (id tăng dần) → ví → kỳ ngân sách. Đọc, tính, lưu trong cùng một
/// DB transaction; cache, cảnh báo, tóm tắt ngày, dự báo chạy sau khi commit. Số dư theo <see cref="TransferConversion"/> (bất biến BR-TRF-07);
/// ngân sách tính lại bằng <see cref="IRefundLedger"/> (Phase 81), nên đổi tháng hay đổi ví không lệch.
/// </summary>
public class ConvertToTransferCommandHandler : IConvertToTransferCommandHandler
{
    public const string FeeDescription = "Phí giao dịch";
    private const string FeeCategorySlug = "other";
    private const string FeeKeyName = "fee";

    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly IAnomalyRepository _anomalies;
    private readonly IMergeSnapshotRepository _snapshots;
    private readonly IRefundLedger _ledger;
    private readonly ISystemConfigProvider _config;
    private readonly RefundAfterSave _afterSave;
    private readonly ITransferRecurringCheck? _recurring;

    public ConvertToTransferCommandHandler(
        ITransactionRepository transactions,
        IFinancialAccountRepository accounts,
        ICategoryRepository categories,
        IAnomalyRepository anomalies,
        IMergeSnapshotRepository snapshots,
        IRefundLedger ledger,
        ISystemConfigProvider config,
        IBudgetAlertNotifier budgetAlerts,
        ICacheService cache,
        IGroupLedger? groupLedger = null,
        IDailySummaryRefresher? summaries = null,
        IForecastAlertTrigger? forecastAlerts = null,
        ITransferRecurringCheck? recurring = null)
    {
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _anomalies = anomalies;
        _snapshots = snapshots;
        _ledger = ledger;
        _config = config;
        _recurring = recurring;
        _afterSave = new RefundAfterSave(budgetAlerts, cache, groupLedger, summaries, forecastAlerts);
    }

    private sealed record Outcome(
        Transaction Transfer, Transaction? Fee, RefundEffects Effects, IReadOnlyList<SnapshotRow> Converted, IReadOnlyDictionary<Guid, long> Deltas);

    public async Task<TransactionPreviewDto> PreviewAsync(ConvertToTransferCommand command, CancellationToken ct = default)
    {
        var ids = command.PairedTransactionId is { } paired ? new[] { command.TransactionId, paired } : [command.TransactionId];
        var outcome = await _transactions.RunPreviewAsync(
            () => _transactions.RunWithTransactionsLockedAsync(command.UserId, ids, rows => ConvertLockedAsync(command, rows, ct), ct), ct);
        var changes = await BalancePreview.ChangesAsync(_accounts, command.UserId, outcome.Deltas, ct);
        var from = await _accounts.GetByIdAsync(outcome.Transfer.FinancialAccountId, command.UserId, ct);
        var cashAfter = changes.FirstOrDefault(c => c.AccountId == from?.Id)?.BalanceAfterCents ?? from?.BalanceCents;
        IReadOnlyList<string>? warnings = from?.AccountType == AccountType.Cash && cashAfter < 0 ? [TransferWarnings.CashBalanceNegative] : null;
        return new TransactionPreviewDto(
            TransactionMapper.ToDto(outcome.Transfer), outcome.Fee is null ? null : TransactionMapper.ToDto(outcome.Fee), changes, warnings);
    }

    public async Task<TransferConversionDto> HandleAsync(ConvertToTransferCommand command, CancellationToken ct = default)
    {
        var ids = command.PairedTransactionId is { } paired ? new[] { command.TransactionId, paired } : [command.TransactionId];
        var outcome = await _transactions.RunWithTransactionsLockedAsync(command.UserId, ids, rows => ConvertLockedAsync(command, rows, ct), ct);

        await _afterSave.RunAsync(command.UserId, outcome.Effects, ct);
        if (_recurring is not null && outcome.Converted.Count > 0)
        {
            await _recurring.AfterConversionAsync(command.UserId, outcome.Transfer, outcome.Converted, ct);
        }

        var from = await _accounts.GetByIdAsync(outcome.Transfer.FinancialAccountId, command.UserId, ct);
        IReadOnlyList<string>? warnings = from is { AccountType: AccountType.Cash, BalanceCents: < 0 } ? [TransferWarnings.CashBalanceNegative] : null;
        return new TransferConversionDto(
            TransactionMapper.ToDto(outcome.Transfer), outcome.Fee is null ? null : TransactionMapper.ToDto(outcome.Fee), warnings);
    }

    private async Task<Outcome> ConvertLockedAsync(ConvertToTransferCommand command, IReadOnlyDictionary<Guid, Transaction> rows, CancellationToken ct)
    {
        var subject = rows[command.TransactionId];
        var other = command.PairedTransactionId is { } pairedId ? rows[pairedId] : null;
        if (await AlreadyConvertedAsync(command, subject, other, ct) is { } done)
        {
            return done;   // gọi lại cùng input → kết quả cũ, không làm gì thêm
        }

        await EnsureConvertibleAsync(command, subject, other, ct);
        var counter = await _accounts.GetByIdAsync(command.CounterAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.CounterAccountId);
        if (counter.Id == subject.FinancialAccountId)
        {
            throw new BusinessRuleException(TransactionErrorCodes.TransferSameAccount, "Ví nguồn và ví đích phải khác nhau.");
        }

        var (debit, credit) = other is null ? (null, null) : Order(subject, other);
        if (other is not null)
        {
            await EnsurePairAsync(command, debit!, credit!, ct);
        }

        var plan = other is null
            ? TransferConversion.OneSided(Side(subject), counter.Id)
            : TransferConversion.Pair(Side(debit!), Side(credit!));
        return await ApplyAsync(command.UserId, plan, subject, other, debit, ct);
    }

    private async Task<Outcome> ApplyAsync(Guid userId, TransferPlan plan, Transaction subject, Transaction? other, Transaction? debit, CancellationToken ct)
    {
        var survivor = subject.Id == plan.SurvivorId ? subject : other!;
        var loser = plan.MergedId is null ? null : (survivor == subject ? other : subject);
        var sides = loser is null ? new[] { survivor } : [survivor, loser];
        var now = DateTimeOffset.UtcNow;
        var before = sides.Where(t => t.Status == TransactionStatus.Confirmed).Select(RefundScopeSnapshot.Of).ToList();
        var rowsBefore = sides.Select(SnapshotRow.Of).ToList();
        var hintKind = await HintedKindAsync(userId, sides, ct);

        foreach (var (accountId, delta) in plan.BalanceDeltas.Where(d => d.Value != 0))
        {
            var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
            account.BalanceCents += delta;
            account.UpdatedAt = now;
        }

        var bankSide = debit ?? (survivor.TransactionType == TransactionType.Debit ? survivor : null);
        Reshape(survivor, plan, bankSide, loser, now);
        var fee = plan.FeeCents > 0 ? await FeeAsync(userId, survivor, debit!, plan, now, ct) : null;
        var released = await ReleaseDraftRefundsAsync(userId, sides, now, ct);
        var repointed = loser is null ? [] : await RepointDuplicateLinksAsync(userId, survivor, loser, now, ct);
        var closed = new List<Guid>();
        foreach (var side in sides)
        {
            if (await _anomalies.ResolveOpenForTransactionAsync(userId, side.Id, AnomalyResolution.Transfer, now, ct) > 0)
            {
                closed.Add(side.Id);
            }
        }

        if (loser is not null)
        {
            loser.DeletedAt = now;
            loser.MergedIntoTransactionId = survivor.Id;
            loser.TransferDismissedAt = null;
            loser.UpdatedAt = now;
        }

        var pending = new List<Transaction> { survivor };
        pending.AddRange(loser is null ? [] : [loser]);
        pending.AddRange(fee is null ? [] : [fee]);
        var effects = await _ledger.RecomputeAsync(userId, pending, before, ct);

        _snapshots.Add(MergeSnapshotFactory.Create(
            userId, survivor.Id, loser?.Id, loser is null ? MergeSnapshotKind.TransferConvert : MergeSnapshotKind.TransferPair, hintKind,
            new SnapshotBefore(rowsBefore, plan.BalanceDeltas, fee?.Id, ReleasedDraftRefunds: released, RepointedDuplicateLinks: repointed, ClosedAnomalies: closed),
            now));
        await SaveAsync(survivor, loser, fee, ct);
        return new Outcome(survivor, fee, effects, rowsBefore, plan.BalanceDeltas);
    }

    /// <summary>
    /// Survivor thành Transfer (BR-TRF-08/09): không danh mục, đã xác nhận, ví nguồn/đích theo kế hoạch, giờ và số dư sau lấy từ chiều tiền ra của ngân hàng.
    /// Loại khỏi dự báo và cờ "không phải chuyển ví" bị xoá (S2, S6).
    /// </summary>
    private static void Reshape(Transaction survivor, TransferPlan plan, Transaction? bankSide, Transaction? loser, DateTimeOffset now)
    {
        survivor.TransactionType = TransactionType.Transfer;
        survivor.FinancialAccountId = plan.FromAccountId;
        survivor.CounterAccountId = plan.ToAccountId;
        survivor.AmountCents = plan.AmountCents;
        survivor.CategoryId = null;
        survivor.Category = null;
        survivor.Status = TransactionStatus.Confirmed;
        survivor.ExcludeFromForecast = false;
        survivor.ForecastExclusionReason = null;
        survivor.TransferDismissedAt = null;
        if (bankSide is not null)
        {
            survivor.TransactedAt = bankSide.TransactedAt;
            survivor.Description ??= bankSide.Description;
        }

        // Số dư sau giao dịch luôn nói về ví nguồn: chiều tiền ra của ngân hàng biết nó; phí (nếu có) mang số đó thay vì Transfer (D13).
        survivor.BalanceAfterCents = plan.FeeCents > 0 ? null : bankSide?.BalanceAfterCents;
        if (loser is not null)
        {
            survivor.MergedSources = Duplicates.MergePolicy.MergedSources(survivor, loser);
            if (survivor.DuplicateOfTransactionId == loser.Id)
            {
                survivor.DuplicateOfTransactionId = null;
                survivor.DuplicateStatus = null;
            }
        }

        survivor.UpdatedAt = now;
    }

    /// <summary>BR-TRF-08: phần chênh thành một khoản chi riêng ở ví nguồn, danh mục <c>other</c>; khoá idempotent = UUID v5(id Transfer, "fee").</summary>
    private async Task<Transaction> FeeAsync(Guid userId, Transaction survivor, Transaction debit, TransferPlan plan, DateTimeOffset now, CancellationToken ct)
    {
        var other = await _categories.GetSystemBySlugAsync(FeeCategorySlug, ct);
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ClientRequestId = DeterministicGuid.V5(survivor.Id, FeeKeyName),
            FinancialAccountId = plan.FromAccountId,
            CategoryId = other?.Id,
            Category = other,
            AmountCents = plan.FeeCents,
            TransactionType = TransactionType.Debit,
            Source = debit.Source,
            Status = TransactionStatus.Confirmed,
            Description = FeeDescription,
            TransactedAt = debit.TransactedAt,
            BalanceAfterCents = debit.BalanceAfterCents,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Nháp hoàn/đảo (Phase 81) đang chờ liên kết với một khoản chi vừa thành Transfer: Transfer không phải khoản chi gốc nên gỡ liên kết.</summary>
    private async Task<List<Guid>> ReleaseDraftRefundsAsync(Guid userId, IEnumerable<Transaction> sides, DateTimeOffset now, CancellationToken ct)
    {
        var released = new List<Guid>();
        foreach (var side in sides.Where(s => s.TransactionType is TransactionType.Debit or TransactionType.Transfer))
        {
            foreach (var draft in await _transactions.ListRelatedAsync(userId, side.Id, ct))
            {
                draft.RelatedTransactionId = null;
                draft.Relation = null;
                draft.UpdatedAt = now;
                released.Add(draft.Id);
            }
        }

        return released;
    }

    private async Task<List<Guid>> RepointDuplicateLinksAsync(Guid userId, Transaction survivor, Transaction loser, DateTimeOffset now, CancellationToken ct)
    {
        var repointed = new List<Guid>();
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
            repointed.Add(linked.Id);
        }

        return repointed;
    }

    /// <summary><c>notification_log_id</c> là duy nhất: chuyển sang survivor thì phải gỡ khỏi bản bị gộp và lưu trước (như Phase 82).</summary>
    private async Task SaveAsync(Transaction survivor, Transaction? loser, Transaction? fee, CancellationToken ct)
    {
        if (loser is not null && survivor.NotificationLogId is null && loser.NotificationLogId is { } log)
        {
            loser.NotificationLogId = null;
            await _transactions.UpdateAsync(loser, ct);
            survivor.NotificationLogId = log;
        }

        if (fee is not null)
        {
            await _transactions.AddAsync(fee, ct);
        }

        await _transactions.UpdateAsync(survivor, ct);
    }

    /// <summary>Gọi lại cùng input: giao dịch đã là Transfer tới đúng ví đó (và chiều kia đã gộp vào nó) → trả lại như cũ.</summary>
    private async Task<Outcome?> AlreadyConvertedAsync(ConvertToTransferCommand command, Transaction subject, Transaction? other, CancellationToken ct)
    {
        var transfer = subject.TransactionType == TransactionType.Transfer && subject.DeletedAt is null ? subject
            : other is { TransactionType: TransactionType.Transfer, DeletedAt: null } ? other
            : null;
        if (transfer is null)
        {
            return null;
        }

        var sameCounter = transfer.FinancialAccountId == command.CounterAccountId || transfer.CounterAccountId == command.CounterAccountId;
        var otherSide = transfer == subject ? other : subject;
        var samePair = otherSide is null || otherSide.MergedIntoTransactionId == transfer.Id;
        if (!sameCounter || !samePair)
        {
            throw Refuse("Giao dịch này đã là chuyển khoản giữa hai ví khác.");
        }

        var fee = await _transactions.GetByClientRequestIdAsync(command.UserId, DeterministicGuid.V5(transfer.Id, FeeKeyName), ct);
        return new Outcome(transfer, fee, RefundEffects.None, [], new Dictionary<Guid, long>());
    }

    private async Task EnsureConvertibleAsync(ConvertToTransferCommand command, Transaction subject, Transaction? other, CancellationToken ct)
    {
        var sides = other is null ? new[] { subject } : [subject, other];
        foreach (var side in sides)
        {
            if (side.MergedIntoTransactionId is not null)
            {
                throw Refuse("Giao dịch đã được gộp vào một giao dịch khác.");
            }

            if (side.DeletedAt is not null)
            {
                throw new NotFoundException("Transaction", side.Id);
            }
        }

        var probes = await _transactions.ListTransferProbesAsync(new TransferProbeQuery(command.UserId, Ids: sides.Select(s => s.Id).ToList()), ct);
        foreach (var probe in probes)
        {
            switch (TransferMatcher.BlockForConversion(probe))
            {
                case null:
                    break;
                case TransferBlock.Shared:
                    throw new BusinessRuleException(
                        TransactionErrorCodes.TransferSharedNotAllowed, "Giao dịch đã chia sẻ vào nhóm. Gỡ khỏi nhóm trước rồi chuyển thành chuyển khoản.");
                case var block:
                    throw Refuse(Reason(block!.Value));
            }
        }
    }

    private async Task EnsurePairAsync(ConvertToTransferCommand command, Transaction debit, Transaction credit, CancellationToken ct)
    {
        if (debit.FinancialAccountId == credit.FinancialAccountId)
        {
            throw Mismatch("Hai chiều phải ở hai ví khác nhau.");
        }

        var otherAccount = debit.Id == command.TransactionId ? credit.FinancialAccountId : debit.FinancialAccountId;
        if (command.CounterAccountId != otherAccount)
        {
            throw Mismatch("Ví đích phải là ví của giao dịch còn lại trong cặp.");
        }

        var settings = (await _config.GetAsync(ct)).Document.Transfers;
        var fee = debit.AmountCents - credit.AmountCents;
        if (fee < 0 || fee > settings.MaxFeeCents || fee * 100 > credit.AmountCents * settings.MaxFeePercent)
        {
            throw Mismatch("Số tiền hai chiều lệch quá mức phí cho phép.");
        }
    }

    private static (Transaction Debit, Transaction Credit) Order(Transaction a, Transaction b)
        => (a.TransactionType, b.TransactionType) switch
        {
            (TransactionType.Debit, TransactionType.Credit) => (a, b),
            (TransactionType.Credit, TransactionType.Debit) => (b, a),
            _ => throw Mismatch("Ghép cặp cần đúng một khoản chi và một khoản thu."),
        };

    private async Task<TransferKind?> HintedKindAsync(Guid userId, IEnumerable<Transaction> sides, CancellationToken ct)
        => (await _transactions.ListTransferProbesAsync(new TransferProbeQuery(userId, Ids: sides.Select(s => s.Id).ToList()), ct))
            .Where(p => p.Hint == NotificationRelationHint.Transfer)
            .Select(p => p.Kind ?? TransferKind.OwnAccount)
            .Cast<TransferKind?>()
            .FirstOrDefault();

    private static ConversionSide Side(Transaction t) => new(t.Id, t.TransactionType, t.Status, t.AmountCents, t.FinancialAccountId, t.CreatedAt);

    private static string Reason(TransferBlock block) => block switch
    {
        TransferBlock.NotDebitOrCredit => "Chỉ khoản chi hoặc khoản thu mới chuyển thành chuyển khoản được.",
        TransferBlock.Relation => "Khoản hoàn tiền hoặc đảo giao dịch không chuyển thành chuyển khoản được.",
        TransferBlock.Reversed => "Khoản chi đã bị đảo không chuyển thành chuyển khoản được.",
        TransferBlock.Merged => "Giao dịch đã được gộp vào một giao dịch khác.",
        TransferBlock.SuspectedDuplicate => "Giao dịch đang bị nghi trùng. Gộp hoặc chọn \"Không trùng\" trước.",
        TransferBlock.QrMatched => "Giao dịch đã khớp một lần quét QR chuyển tiền cho người khác.",
        TransferBlock.HasRefunds => "Khoản chi đã có khoản hoàn. Xoá các khoản đó trước.",
        _ => "Giao dịch này không chuyển thành chuyển khoản được.",
    };

    private static BusinessRuleException Refuse(string reason) => new(TransactionErrorCodes.TransferNotAllowed, reason);

    private static BusinessRuleException Mismatch(string reason) => new(TransactionErrorCodes.TransferPairMismatch, reason);
}

/// <summary>
/// S3: sau khi chuyển đổi, nếu một chiều đã khớp một khoản định kỳ và Transfer nằm trọn trong các ví được tính vào dự báo, hỏi người dùng "còn dùng
/// không?" (luồng FR-FCST-12), không tự tạm dừng hay huỷ. Chạy SAU khi lưu; không bao giờ ném.
/// </summary>
public interface ITransferRecurringCheck
{
    /// <param name="before">Các chiều như TRƯỚC khi chuyển đổi (loại, nơi chi, thời điểm) để so với khoản định kỳ.</param>
    Task AfterConversionAsync(Guid userId, Transaction transfer, IReadOnlyList<SnapshotRow> before, CancellationToken ct = default);
}
