using FinMate.Application.Common;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>Chữ cố định của giao dịch do sổ vay tạo (Q3): KHÔNG chép tên người hay tiêu đề khoản vào giao dịch.</summary>
public static class LoanTransactionText
{
    public const string InterestPaid = "Lãi vay";
    public const string InterestReceived = "Tiền lãi";

    public static string For(LoanDirection direction, LoanEntryKind kind) => (direction, kind) switch
    {
        (LoanDirection.Lend, LoanEntryKind.Principal) => "Cho vay",
        (LoanDirection.Lend, LoanEntryKind.Repayment) => "Thu nợ",
        (LoanDirection.Borrow, LoanEntryKind.Principal) => "Đi vay",
        _ => "Trả nợ",
    };
}

/// <summary>
/// Phần giao dịch của sổ vay (Phase 90, chép từ <c>DebtTransactionLink</c> của Phase 86 cũ), dùng chung giữa tạo khoản, trả / giải ngân thêm và
/// gắn giao dịch: dựng giao dịch <c>loan</c> mới, giao dịch lãi tách ra (S1), và đưa một giao dịch có sẵn về danh mục <c>loan</c> (BR-DEBT-06, D21)
/// bằng cơ chế tính lại kỳ của Phase 81. Chỉ đổi entity đang theo dõi; không lưu.
/// </summary>
public sealed class LoanTransactionLink(IRefundLedger ledger, IDuplicateDetector? duplicates = null)
{
    public const string InterestCategoryForBorrow = "other";
    public const string InterestCategoryForLend = "income";

    /// <summary>Khoá idempotent của giao dịch lãi tách từ một dòng trả (S1): UUID v5(id dòng, "interest"), như phí chuyển ví của Phase 83.</summary>
    public static Guid InterestClientRequestIdFor(Guid entryId) => DeterministicGuid.V5(entryId, "interest");

    /// <summary>Slug danh mục của giao dịch lãi: đi vay thì lãi là khoản chi <c>other</c>, cho vay thì là khoản thu <c>income</c> (S1).</summary>
    public static string InterestCategorySlug(LoanDirection direction) => direction == LoanDirection.Borrow ? InterestCategoryForBorrow : InterestCategoryForLend;

    /// <summary>
    /// Giao dịch đã xác nhận mới, danh mục <c>loan</c>, chiều theo BR-DEBT-05, chữ cố định (Q3), cộng/trừ số dư ví ngay. Có so trùng (Phase 82,
    /// BR-DUP-06): ngân hàng có thể đã báo khoản này, khi đó nó mang nghi trùng để app hỏi Gộp (kịch bản h).
    /// </summary>
    public async Task<Transaction> NewAsync(
        Guid userId, Loan loan, LoanEntryKind kind, long amountCents, FinancialAccount account, Category loanCategory, DateTimeOffset transactedAt,
        DateTimeOffset now, CancellationToken ct)
    {
        var transaction = Build(
            userId, account, loanCategory, amountCents, LoanPolicy.TransactionTypeFor(loan.Direction, kind), LoanTransactionText.For(loan.Direction, kind), transactedAt, now);
        if (duplicates is not null)
        {
            await duplicates.MarkSuspectedAsync(transaction, null, null, ct);
        }

        return transaction;
    }

    /// <summary>
    /// S1: phần lãi của một lần trả, thành giao dịch riêng KHÔNG thuộc <c>loan</c> (đi vay: chi <c>other</c> "Lãi vay"; cho vay: thu <c>income</c>
    /// "Tiền lãi"), nên được tính vào chi tiêu / thu nhập thật (Q16). <paramref name="moveBalance"/>: tạo kèm một lần trả qua ví thì cộng/trừ số dư;
    /// tách từ một giao dịch có sẵn thì số dư đã đúng (giao dịch gốc đã mang cả số), nên không đụng.
    /// </summary>
    public static Transaction NewInterest(
        Guid userId, Loan loan, Guid entryId, long interestCents, FinancialAccount account, Category? category, DateTimeOffset transactedAt, bool moveBalance,
        TransactionSource source, DateTimeOffset now)
    {
        var type = LoanPolicy.TransactionTypeFor(loan.Direction, LoanEntryKind.Repayment);
        var text = loan.Direction == LoanDirection.Borrow ? LoanTransactionText.InterestPaid : LoanTransactionText.InterestReceived;
        var transaction = moveBalance
            ? Build(userId, account, category, interestCents, type, text, transactedAt, now)
            : new Transaction
            {
                Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = account.Id, CategoryId = category?.Id, Category = category, AmountCents = interestCents,
                TransactionType = type, Status = TransactionStatus.Confirmed, Description = text, TransactedAt = transactedAt, CreatedAt = now, UpdatedAt = now,
            };
        transaction.ClientRequestId = InterestClientRequestIdFor(entryId);
        transaction.Source = source;
        return transaction;
    }

    /// <summary>BR-DEBT-06, D21: đổi sang <c>loan</c> và tính lại các kỳ ngân sách bị chạm (danh mục cũ lẫn mới). Đã là <c>loan</c> thì không làm gì.</summary>
    public async Task<RefundEffects> MoveToLoanAsync(Guid userId, Transaction transaction, Category loanCategory, DateTimeOffset now, CancellationToken ct)
    {
        if (transaction.CategoryId == loanCategory.Id)
        {
            return RefundEffects.None;
        }

        var before = RefundScopeSnapshot.Of(transaction);
        transaction.CategoryId = loanCategory.Id;
        transaction.Category = loanCategory;
        transaction.UpdatedAt = now;
        return await ledger.RecomputeAsync(userId, [transaction], [before], ct);
    }

    /// <summary>Giao dịch lãi mới chạm ngân sách của danh mục nó (khoản chi <c>other</c>): tính lại kỳ đó trước khi lưu.</summary>
    public Task<RefundEffects> RecomputeForNewAsync(Guid userId, Transaction transaction, CancellationToken ct)
        => ledger.RecomputeAsync(userId, [transaction], [], ct);

    /// <summary>Việc sau lưu cho giao dịch <c>loan</c> mới: không chạm ngân sách nào, nhưng ngày đó cần chốt lại tóm tắt và dự báo cần chạy lại.</summary>
    public static RefundEffects Touched(Transaction transaction) => new([], [], [VietnamTime.DateOf(transaction.TransactedAt)]);

    public static RefundEffects Merge(RefundEffects a, RefundEffects b)
        => new([.. a.BudgetAlerts, .. b.BudgetAlerts], [.. a.GroupAlerts, .. b.GroupAlerts], a.TouchedDates.Union(b.TouchedDates).ToList());

    private static Transaction Build(
        Guid userId, FinancialAccount account, Category? category, long amountCents, TransactionType type, string description, DateTimeOffset transactedAt,
        DateTimeOffset now)
    {
        account.BalanceCents += type == TransactionType.Credit ? amountCents : -amountCents;
        account.UpdatedAt = now;
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FinancialAccountId = account.Id,
            CategoryId = category?.Id,
            Category = category,
            AmountCents = amountCents,
            TransactionType = type,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            Description = description,
            TransactedAt = transactedAt,
            BalanceAfterCents = account.BalanceCents,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
