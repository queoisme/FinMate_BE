using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>Liên kết khoản vay của một giao dịch, cho DTO giao dịch (<c>loanLink</c>) và kết quả xác nhận.</summary>
public sealed record LoanLinkDto(Guid LoanId, string Title, string Counterparty, string Kind);

/// <summary>Kết quả gắn lúc xác nhận: liên kết, và phần lãi tách ra (S1) cùng việc sau lưu của nó (cảnh báo ngân sách <c>other</c>).</summary>
public sealed record LoanAttachResult(LoanLinkDto Link, Transaction? Interest, RefundEffects Effects);

/// <summary>
/// Giữ sổ vay khớp với các đường giao dịch có sẵn (Phase 90, Task 7; chép từ <c>DebtLinkSync</c> của Phase 86 cũ): xoá (BR-DEBT-09, D5), sửa (D3),
/// gộp trùng (D7, Q14), chuyển ví (D7), xác nhận kèm khoản (D4, S1). Chỉ đổi entity đang theo dõi, ném TRƯỚC khi đổi gì; handler gọi nó lưu cùng
/// lượt của mình. Là tham số tuỳ chọn trong handler (như <c>IGroupFundLinkSync</c>), nên test cũ không cần biết tới.
/// </summary>
public interface ILoanLinkSync
{
    /// <summary>BR-DEBT-09: giao dịch sắp bị xoá mềm → dòng của nó đi theo, khoản tính lại; làm Σ giải ngân &lt; Σ trả thì 422.</summary>
    Task OnDeletingAsync(Guid userId, Transaction transaction, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// D3: giao dịch đã gắn sắp được sửa. Đổi danh mục khỏi <c>loan</c> hoặc đổi loại → 422 <c>TRANSACTION_LOAN_LINKED</c>; đổi số tiền → kiểm và
    /// cập nhật tổng; đổi ngày → ngày của dòng đi theo.
    /// </summary>
    Task OnUpdatingAsync(
        Guid userId, Transaction transaction, Category? newCategory, TransactionType newType, long newAmountCents, DateTimeOffset newTransactedAt,
        DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// D4: nháp sắp chuyển sang Confirmed được gắn vào khoản: kiểm điều kiện, danh mục thành <c>loan</c>, thêm dòng. Trả vượt gốc còn lại trong ngưỡng
    /// lãi (S1): giao dịch giữ phần gốc, phần lãi thành giao dịch riêng đã xác nhận, cộng/trừ số dư <paramref name="account"/> ngay (handler áp phần
    /// gốc sau). Loại dòng suy từ chiều khoản và loại giao dịch (BR-DEBT-05).
    /// </summary>
    Task<LoanAttachResult> AttachOnConfirmAsync(
        Guid userId, Transaction transaction, FinancialAccount account, Guid loanId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>D7: chuyển ví không áp cho giao dịch đã gắn.</summary>
    Task EnsureNotLinkedAsync(Guid userId, IEnumerable<Guid> transactionIds, CancellationToken ct = default);

    /// <summary>Q14: phần lãi đã tách khỏi giao dịch này (để so số tiền khi gộp); 0 khi không có.</summary>
    Task<long> SplitInterestOfAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>
    /// D7, Q14: gộp trùng, gọi TRƯỚC khi chuyển số dư. Hai bên cùng gắn → 422. Bên thua có liên kết: bên thắng phải cùng loại, không quan hệ, không
    /// nhóm, không quỹ, rồi dòng chuyển sang bên thắng. Dòng đã tách lãi: bên thắng lấy số tiền phần gốc (phần lãi vẫn là giao dịch riêng, nên số dư
    /// đúng bằng số ngân hàng báo); không tách: dòng lấy số tiền của bên thắng. Trả true khi bên thắng mang liên kết: caller đưa nó về <c>loan</c>
    /// SAU khi áp các trường gộp.
    /// </summary>
    Task<bool> MergeAsync(Guid userId, Transaction loser, Transaction survivor, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class LoanLinkSync(ILoanRepository loans, ICategoryRepository categories, LoanTransactionLink link, ISystemConfigProvider? config = null)
    : ILoanLinkSync
{
    public async Task<LoanAttachResult> AttachOnConfirmAsync(
        Guid userId, Transaction transaction, FinancialAccount account, Guid loanId, DateTimeOffset now, CancellationToken ct = default)
    {
        var loan = await loans.GetOwnedAsync(loanId, userId, ct) ?? throw new NotFoundException("Loan", loanId);
        LoanPolicy.EnsureEligible(transaction, await loans.IsTransactionLinkedAsync(userId, transaction.Id, ct), beingConfirmed: true);
        var kind = LoanPolicy.TransactionTypeFor(loan.Direction, LoanEntryKind.Repayment) == transaction.TransactionType
            ? LoanEntryKind.Repayment
            : LoanEntryKind.Principal;

        var amount = transaction.AmountCents;
        var interest = 0L;
        if (kind == LoanEntryKind.Repayment)
        {
            var tolerance = config is null ? new LoanSettings().InterestToleranceCents : (await config.GetAsync(ct)).Document.Loans.InterestToleranceCents;
            (amount, interest) = LoanPolicy.SplitRepayment(loan, amount, LoanInterest.Accrued(loan, Common.VietnamTime.DateOf(transaction.TransactedAt)), tolerance);
        }

        var loanCategory = await categories.GetSystemBySlugAsync(Category.LoanSlug, ct) ?? throw new InvalidOperationException("Thiếu danh mục hệ thống 'loan'.");
        LoanPolicy.Add(loan, kind, amount, now);
        var entry = new LoanRepayment
        {
            Id = Guid.NewGuid(), LoanId = loan.Id, Kind = kind, AmountCents = amount, PaidAt = transaction.TransactedAt, TransactionId = transaction.Id, CreatedAt = now,
        };

        Transaction? interestTransaction = null;
        var effects = RefundEffects.None;
        if (interest > 0)
        {
            var interestCategory = await categories.GetSystemBySlugAsync(LoanTransactionLink.InterestCategorySlug(loan.Direction), ct);
            interestTransaction = LoanTransactionLink.NewInterest(
                userId, loan, entry.Id, interest, account, interestCategory, transaction.TransactedAt, moveBalance: true, transaction.Source, now);
            interestTransaction.BalanceAfterCents = null;   // số dư ngân hàng báo nằm trên giao dịch gốc
            entry.InterestTransactionId = interestTransaction.Id;
            transaction.AmountCents = amount;
            loans.Track(interestTransaction);
            effects = await link.RecomputeForNewAsync(userId, interestTransaction, ct);
        }

        transaction.CategoryId = loanCategory.Id;
        transaction.Category = loanCategory;
        loan.Repayments.Add(entry);
        loans.Track(entry);
        return new LoanAttachResult(new LoanLinkDto(loan.Id, loan.Title, loan.Counterparty, KindName(kind)), interestTransaction, effects);
    }

    public async Task OnDeletingAsync(Guid userId, Transaction transaction, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await loans.GetByLinkedTransactionAsync(userId, transaction.Id, ct) is not { } loan)
        {
            return;
        }

        var entry = Entry(loan, transaction.Id);
        var emptied = LoanPolicy.Remove(loan, entry.Kind, entry.AmountCents, now);
        entry.DeletedAt = now;
        if (emptied)
        {
            loan.DeletedAt = now;   // D5
        }
    }

    public async Task OnUpdatingAsync(
        Guid userId, Transaction transaction, Category? newCategory, TransactionType newType, long newAmountCents, DateTimeOffset newTransactedAt,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (await loans.GetByLinkedTransactionAsync(userId, transaction.Id, ct) is not { } loan)
        {
            return;
        }

        if (newCategory?.Slug != Category.LoanSlug || newType != transaction.TransactionType)
        {
            throw Linked();
        }

        var entry = Entry(loan, transaction.Id);
        if (newAmountCents != entry.AmountCents)
        {
            LoanPolicy.ChangeAmount(loan, entry.Kind, entry.AmountCents, newAmountCents, now);
            entry.AmountCents = newAmountCents;
        }

        entry.PaidAt = newTransactedAt;
    }

    public async Task EnsureNotLinkedAsync(Guid userId, IEnumerable<Guid> transactionIds, CancellationToken ct = default)
    {
        foreach (var id in transactionIds.Distinct())
        {
            if (await loans.IsTransactionLinkedAsync(userId, id, ct))
            {
                throw Linked();
            }
        }
    }

    public Task<long> SplitInterestOfAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
        => loans.GetSplitInterestCentsAsync(userId, transactionId, ct);

    public async Task<bool> MergeAsync(Guid userId, Transaction loser, Transaction survivor, DateTimeOffset now, CancellationToken ct = default)
    {
        var loserLoan = await loans.GetByLinkedTransactionAsync(userId, loser.Id, ct);
        var survivorLoan = await loans.GetByLinkedTransactionAsync(userId, survivor.Id, ct);
        if (loserLoan is not null && survivorLoan is not null)
        {
            throw Linked();
        }

        if (loserLoan is not null
            && (survivor.TransactionType != loser.TransactionType || survivor.Relation is not null || survivor.GroupId is not null || survivor.GroupFundEntryId is not null))
        {
            throw Linked();
        }

        var loan = loserLoan ?? survivorLoan;
        if (loan is null)
        {
            return false;
        }

        var entry = Entry(loan, (loserLoan is not null ? loser : survivor).Id);
        if (entry.InterestTransactionId is not null)
        {
            survivor.AmountCents = entry.AmountCents;   // Q14: phần lãi đã là giao dịch riêng; bên thắng chỉ mang phần gốc
        }
        else if (survivor.AmountCents != entry.AmountCents)
        {
            LoanPolicy.ChangeAmount(loan, entry.Kind, entry.AmountCents, survivor.AmountCents, now);
            entry.AmountCents = survivor.AmountCents;
        }

        entry.PaidAt = survivor.TransactedAt;
        entry.TransactionId = survivor.Id;   // chỉ một dòng đổi và bên kia chưa gắn: UNIQUE không vướng
        return true;
    }

    private static LoanRepayment Entry(Loan loan, Guid transactionId)
        => loan.Repayments.First(r => r.DeletedAt is null && r.TransactionId == transactionId);

    private static string KindName(LoanEntryKind kind) => kind == LoanEntryKind.Principal ? "principal" : "repayment";

    private static BusinessRuleException Linked()
        => new(LoanErrorCodes.TransactionLinked, "Giao dịch này đang ghi một khoản vay. Gỡ khỏi sổ vay trước rồi mới đổi.");
}
