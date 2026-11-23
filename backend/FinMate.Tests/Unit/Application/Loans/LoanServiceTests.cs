using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Loans;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

/// <summary>
/// Sổ vay. Các test trước Phase 90 giữ nguyên kỳ vọng; Phase 90 (Task 5) đổi cách ghi: giao dịch ví do sổ vay tự dựng (không qua
/// <c>CreateManualTransactionCommand</c>) và lưu cùng dòng trong MỘT lần, nên test "ví lỗi thì gỡ lần trả đã ghi" thành "ví không có thì không ghi gì".
/// </summary>
public class LoanServiceTests
{
    private readonly Mock<ILoanRepository> _loans = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<IRefundLedger> _ledger = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly LoanService _service;
    private readonly List<Transaction> _tracked = [];
    private readonly Category _loanCategory = new() { Id = Guid.NewGuid(), Slug = Category.LoanSlug };

    public LoanServiceTests()
    {
        _service = new LoanService(new LoanServiceDependencies(
            _loans.Object, _categories.Object, _transactions.Object, _accounts.Object, new LoanTransactionLink(_ledger.Object)));
        _loans.Setup(r => r.Track(It.IsAny<Transaction>())).Callback<Transaction>(_tracked.Add);
        _categories.Setup(c => c.GetSystemBySlugAsync(Category.LoanSlug, It.IsAny<CancellationToken>())).ReturnsAsync(_loanCategory);
        _ledger.Setup(l => l.RecomputeAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Transaction>>(), It.IsAny<IReadOnlyCollection<RefundScopeSnapshot>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RefundEffects.None);
    }

    private Loan Existing(LoanDirection direction, long principal, long paid = 0)
    {
        var loan = new Loan { Id = Guid.NewGuid(), UserId = _userId, Direction = direction, Title = "Xe", Counterparty = "FE", PrincipalCents = principal };
        if (paid > 0)
        {
            loan.Repayments.Add(new LoanRepayment { Id = Guid.NewGuid(), LoanId = loan.Id, AmountCents = paid });
        }

        LoanTotals.Recompute(loan, DateTimeOffset.UtcNow);   // tổng lưu sẵn như sau migration
        _loans.Setup(r => r.GetOwnedAsync(loan.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(loan);
        return loan;
    }

    private FinancialAccount Wallet(long balance = 50_000_000)
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = _userId, BalanceCents = balance };
        _accounts.Setup(a => a.GetByIdAsync(account.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        return account;
    }

    [Theory]
    [InlineData("", "FE", 100, 0)]
    [InlineData("Xe", "", 100, 0)]
    [InlineData("Xe", "FE", 0, 0)]
    [InlineData("Xe", "FE", 100, 101)]
    [InlineData("Xe", "FE", 100, -1)]
    public async Task Create_InvalidInput_Rejected(string title, string who, long principal, decimal rate)
    {
        var act = () => _service.CreateAsync(_userId, new LoanInput(LoanDirection.Borrow, title, who, principal, rate, null));
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.Invalid);
        _loans.Verify(r => r.AddAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Repay_Borrow_WithWallet_CreatesLoanCategoryDebit_AndReducesRemaining()
    {
        var loan = Existing(LoanDirection.Borrow, 30_000_000, paid: 18_000_000);
        var wallet = Wallet();

        var dto = (await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(2_000_000, wallet.Id))).Loan;

        var sent = _tracked.Should().ContainSingle().Subject;
        sent.TransactionType.Should().Be(TransactionType.Debit);
        sent.CategoryId.Should().Be(_loanCategory.Id);
        sent.FinancialAccountId.Should().Be(wallet.Id);
        (sent.MerchantName, sent.Description).Should().Be(((string?)null, "Trả nợ"), "Q3: không chép tên người / tiêu đề khoản vào giao dịch");
        wallet.BalanceCents.Should().Be(48_000_000);
        dto.PaidCents.Should().Be(20_000_000);
        dto.RemainingCents.Should().Be(10_000_000);
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Once, "một lần lưu cho dòng, ví và giao dịch");
        loan.Repayments.Last().TransactionId.Should().Be(sent.Id);
    }

    [Fact]
    public async Task Repay_Lend_IsCredit_AndWithoutWalletTouchesNoTransaction()
    {
        var loan = Existing(LoanDirection.Lend, 15_000_000);
        await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(5_000_000));
        _tracked.Should().BeEmpty();

        await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(1_000_000, Wallet().Id));
        _tracked.Should().ContainSingle().Which.TransactionType.Should().Be(TransactionType.Credit);
    }

    [Fact]
    public async Task Repay_MoreThanRemaining_Rejected_NothingWritten()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000, paid: 900);
        var act = () => _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(200, Wallet().Id));
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.Overpay);
        _tracked.Should().BeEmpty();
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_PrincipalBelowPaid_Rejected()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000, paid: 800);
        var act = () => _service.UpdateAsync(_userId, loan.Id, new LoanInput(LoanDirection.Borrow, "Xe", "FE", 500, 0, null));
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.PrincipalBelowPaid);
    }

    [Fact]
    public async Task Repay_OtherUsersLoan_NotFound()
    {
        var act = () => _service.RepayAsync(_userId, Guid.NewGuid(), new LoanEntryInput(100));
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Repay_UnknownWallet_NotFound_NothingWritten()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000);

        var act = () => _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(100, Guid.NewGuid()));

        await act.Should().ThrowAsync<NotFoundException>();
        loan.Repayments.Should().BeEmpty();
        (loan.RepaidCents, _tracked.Count).Should().Be((0L, 0));
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Interest_SimpleDaily_OnOutstandingPrincipal_ReducedByRepayments()
    {
        var start = new DateOnly(2026, 1, 1);
        var loan = new Loan { PrincipalCents = 36_500_000, InterestRatePercentYear = 10, StartDate = start };

        // 100 ngày, chưa trả: 36.5tr × 10% / 365 × 100 = 1.000.000
        LoanInterest.Accrued(loan, start.AddDays(100)).Should().Be(1_000_000);

        // Trả 18.25tr vào ngày 100: 100 ngày đầu 1.000.000, 100 ngày sau trên gốc còn một nửa = 500.000
        loan.Repayments.Add(new LoanRepayment { AmountCents = 18_250_000, PaidAt = new DateTimeOffset(2026, 4, 11, 12, 0, 0, TimeSpan.Zero) });
        LoanInterest.Accrued(loan, new DateOnly(2026, 4, 11).AddDays(100)).Should().Be(1_500_000);

        loan.InterestRatePercentYear = 0;
        LoanInterest.Accrued(loan, start.AddDays(100)).Should().Be(0);
    }

    // ---- Phase 90, Task 3: dòng giải ngân, tổng lưu sẵn, trạng thái, S2 ----

    private Loan WithEntries(long firstPrincipal, long secondPrincipal = 0, long repaid = 0, Guid? firstTransaction = null)
    {
        var start = new DateOnly(2026, 9, 1);
        var loan = new Loan { Id = Guid.NewGuid(), UserId = _userId, Direction = LoanDirection.Lend, Title = "Xe", Counterparty = "FE", StartDate = start };
        loan.Repayments.Add(new LoanRepayment
        {
            Id = Guid.NewGuid(), LoanId = loan.Id, Kind = LoanEntryKind.Principal, AmountCents = firstPrincipal, TransactionId = firstTransaction,
            PaidAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(7)),
        });
        if (secondPrincipal > 0)
        {
            loan.Repayments.Add(new LoanRepayment
            {
                Id = Guid.NewGuid(), LoanId = loan.Id, Kind = LoanEntryKind.Principal, AmountCents = secondPrincipal, PaidAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            });
        }

        if (repaid > 0)
        {
            loan.Repayments.Add(new LoanRepayment { Id = Guid.NewGuid(), LoanId = loan.Id, AmountCents = repaid, PaidAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero) });
        }

        LoanTotals.Recompute(loan, DateTimeOffset.UtcNow);
        _loans.Setup(r => r.GetOwnedAsync(loan.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(loan);
        return loan;
    }

    [Fact]
    public async Task Create_AddsOnePrincipalEntry_OnTheStartDate_WithoutATransaction()
    {
        Loan? saved = null;
        _loans.Setup(r => r.AddAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>())).Callback<Loan, CancellationToken>((l, _) => saved = l);

        var dto = (await _service.CreateAsync(_userId, new LoanInput(LoanDirection.Lend, "Xe", "FE", 2_000_000, 0, null, new DateOnly(2026, 9, 1)))).Loan;

        var entry = saved!.Repayments.Should().ContainSingle().Subject;
        (entry.Kind, entry.AmountCents, entry.TransactionId).Should().Be((LoanEntryKind.Principal, 2_000_000L, (Guid?)null));
        entry.PaidAt.Should().Be(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(7)));
        (saved.PrincipalCents, saved.RepaidCents, saved.Status).Should().Be((2_000_000L, 0L, LoanStatus.Open));
        (dto.PaidCents, dto.RemainingCents).Should().Be((0L, 2_000_000L), "dòng giải ngân không phải lần trả");
    }

    [Fact]
    public async Task Update_PrincipalIsTheNewTotal_TheFirstEntryMovesByTheDifference_WithTwoDisbursements()
    {
        var loan = WithEntries(1_000_000, secondPrincipal: 500_000, repaid: 200_000);

        var dto = await _service.UpdateAsync(_userId, loan.Id, new LoanInput(LoanDirection.Lend, "Xe", "FE", 1_800_000, 0, null));

        loan.Live(LoanEntryKind.Principal).Select(e => e.AmountCents).Should().Equal(1_300_000, 500_000);
        (loan.PrincipalCents, loan.RepaidCents, dto.RemainingCents).Should().Be((1_800_000L, 200_000L, 1_600_000L));
    }

    [Fact]
    public async Task Update_FirstEntryLinkedToATransaction_Rejected()
    {
        var loan = WithEntries(1_000_000, firstTransaction: Guid.NewGuid());

        var act = () => _service.UpdateAsync(_userId, loan.Id, new LoanInput(LoanDirection.Lend, "Xe", "FE", 1_200_000, 0, null));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.PrincipalLinked);
    }

    [Fact]
    public async Task Update_FirstEntryWouldDropToZero_Rejected()
    {
        var loan = WithEntries(300_000, secondPrincipal: 500_000);

        var act = () => _service.UpdateAsync(_userId, loan.Id, new LoanInput(LoanDirection.Lend, "Xe", "FE", 500_000, 0, null));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.Invalid);
    }

    [Fact]
    public async Task Repay_TheRest_SettlesTheLoan()
    {
        var loan = WithEntries(1_000_000, repaid: 400_000);

        await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(600_000));

        (loan.RepaidCents, loan.Status).Should().Be((1_000_000L, LoanStatus.Settled));
        loan.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public void Interest_ASecondDisbursement_RaisesTheOutstandingFromItsDate()
    {
        var loan = WithEntries(36_500_000, secondPrincipal: 36_500_000);
        loan.InterestRatePercentYear = 10;

        // 30 ngày trên 36,5tr (10.000/ngày) rồi 30 ngày trên 73tr (20.000/ngày).
        LoanInterest.Accrued(loan, new DateOnly(2026, 10, 31)).Should().Be(30 * 10_000 + 30 * 20_000);
    }

    // ---- Phase 90, Task 5: S1, gắn giao dịch, xoá dòng, xoá mềm, xoá nợ, gửi lại ----

    private Loan Interesting(LoanDirection direction = LoanDirection.Borrow)
    {
        var loan = WithEntries(2_000_000);
        loan.Direction = direction;
        loan.InterestRatePercentYear = 12;
        return loan;
    }

    private static readonly DateTimeOffset NinetyDaysIn = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(7));   // 1/9 + 90 ngày

    [Fact]
    public async Task ScenarioI_Repay2060000_At12Percent_After90Days_SettlesAndSplits60000AsASpend()
    {
        var loan = Interesting();
        var wallet = Wallet(10_000_000);
        var other = new Category { Id = Guid.NewGuid(), Slug = "other" };
        _categories.Setup(c => c.GetSystemBySlugAsync("other", It.IsAny<CancellationToken>())).ReturnsAsync(other);

        var dto = (await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(2_060_000, wallet.Id, NinetyDaysIn))).Loan;

        loan.Status.Should().Be(LoanStatus.Settled);
        dto.RemainingCents.Should().Be(0);
        _tracked.Select(t => (t.AmountCents, t.CategoryId, t.Description, t.TransactionType))
            .Should().Equal((2_000_000L, _loanCategory.Id, "Trả nợ", TransactionType.Debit), (60_000L, other.Id, "Lãi vay", TransactionType.Debit));
        var entry = loan.Live(LoanEntryKind.Repayment).Single();
        (entry.AmountCents, entry.TransactionId, entry.InterestTransactionId).Should().Be((2_000_000L, _tracked[0].Id, _tracked[1].Id));
        _tracked[1].ClientRequestId.Should().Be(LoanTransactionLink.InterestClientRequestIdFor(entry.Id), "idempotent qua UUID v5");
        wallet.BalanceCents.Should().Be(10_000_000 - 2_060_000);
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Repay_Lend_InterestIsIncome_TienLai()
    {
        var loan = Interesting(LoanDirection.Lend);
        var income = new Category { Id = Guid.NewGuid(), Slug = "income" };
        _categories.Setup(c => c.GetSystemBySlugAsync("income", It.IsAny<CancellationToken>())).ReturnsAsync(income);

        await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(2_060_000, Wallet().Id, NinetyDaysIn));

        _tracked[1].Should().Match<Transaction>(t => t.CategoryId == income.Id && t.Description == "Tiền lãi" && t.TransactionType == TransactionType.Credit);
    }

    [Fact]
    public async Task Repay_ExcessBeyondInterestAndTolerance_IsRejected()
    {
        var loan = Interesting();

        var act = () => _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(2_080_000, Wallet().Id, NinetyDaysIn));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.Overpay);
        _tracked.Should().BeEmpty();
    }

    [Fact]
    public async Task Repay_SameClientRequestTwice_WritesOnce()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000_000);
        var request = Guid.NewGuid();
        _loans.Setup(r => r.EntryClientRequestExistsAsync(loan.Id, request, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.RepayAsync(_userId, loan.Id, new LoanEntryInput(100_000, null, null, request));

        result.AlreadyExisted.Should().BeTrue();
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private Transaction Locked(Guid accountId, long amount, TransactionType type, Category? category = null)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, FinancialAccountId = accountId, AmountCents = amount, TransactionType = type, Status = TransactionStatus.Confirmed,
            Source = TransactionSource.Notification, CategoryId = category?.Id, Category = category, TransactedAt = NinetyDaysIn,
        };
        _transactions.Setup(t => t.RunWithTransactionsLockedAsync(_userId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<IReadOnlyDictionary<Guid, Transaction>, Task<(Loan, RefundEffects)>>>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyCollection<Guid>, Func<IReadOnlyDictionary<Guid, Transaction>, Task<(Loan, RefundEffects)>>, CancellationToken>(
                (_, _, work, _) => work(new Dictionary<Guid, Transaction> { [transaction.Id] = transaction }));
        _transactions.Setup(t => t.RunWithTransactionsLockedAsync(_userId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<Func<IReadOnlyDictionary<Guid, Transaction>, Task<RefundEffects>>>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyCollection<Guid>, Func<IReadOnlyDictionary<Guid, Transaction>, Task<RefundEffects>>, CancellationToken>(
                (_, _, work, _) => work(new Dictionary<Guid, Transaction> { [transaction.Id] = transaction }));
        return transaction;
    }

    [Fact]
    public async Task Link_ABankNotificationOf2060000_SplitsIntoLoanAndInterest_BalanceUnchanged()
    {
        var loan = Interesting();
        var wallet = Wallet(10_000_000);
        var food = new Category { Id = Guid.NewGuid(), Slug = "food" };
        var other = new Category { Id = Guid.NewGuid(), Slug = "other" };
        _categories.Setup(c => c.GetSystemBySlugAsync("other", It.IsAny<CancellationToken>())).ReturnsAsync(other);
        var transaction = Locked(wallet.Id, 2_060_000, TransactionType.Debit, food);

        var dto = await _service.LinkAsync(_userId, loan.Id, transaction.Id, LoanEntryKind.Repayment);

        dto.RemainingCents.Should().Be(0);
        (transaction.AmountCents, transaction.CategoryId).Should().Be((2_000_000L, _loanCategory.Id));
        var interest = _tracked.Should().ContainSingle().Subject;
        (interest.AmountCents, interest.CategoryId, interest.FinancialAccountId, interest.Source).Should().Be((60_000L, other.Id, wallet.Id, TransactionSource.Notification));
        wallet.BalanceCents.Should().Be(10_000_000, "giao dịch ngân hàng đã mang cả số; tách ra không đổi số dư");
        loan.Live(LoanEntryKind.Repayment).Single().InterestTransactionId.Should().Be(interest.Id);
    }

    [Fact]
    public async Task Link_WrongDirection_Rejected()
    {
        var loan = Existing(LoanDirection.Lend, 1_000_000);
        var transaction = Locked(Guid.NewGuid(), 500_000, TransactionType.Debit);

        var act = () => _service.LinkAsync(_userId, loan.Id, transaction.Id, LoanEntryKind.Repayment);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.DirectionMismatch);
    }

    [Fact]
    public async Task Link_AlreadyLinkedElsewhere_Rejected()
    {
        var loan = Existing(LoanDirection.Lend, 1_000_000);
        var transaction = Locked(Guid.NewGuid(), 500_000, TransactionType.Credit);
        _loans.Setup(r => r.IsTransactionLinkedAsync(_userId, transaction.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.LinkAsync(_userId, loan.Id, transaction.Id, LoanEntryKind.Repayment);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.TransactionNotEligible);
    }

    [Fact]
    public async Task DeleteEntry_ReopensASettledLoan_AndTheLastPrincipalEmptiesIt()
    {
        var loan = WithEntries(1_000_000, repaid: 1_000_000);
        loan.Status.Should().Be(LoanStatus.Settled);
        var repayment = loan.Live(LoanEntryKind.Repayment).Single();

        var dto = await _service.DeleteEntryAsync(_userId, loan.Id, repayment.Id);

        (dto!.RemainingCents, loan.Status).Should().Be((1_000_000L, LoanStatus.Open));
        repayment.DeletedAt.Should().NotBeNull();
        (await _service.DeleteEntryAsync(_userId, loan.Id, loan.Live(LoanEntryKind.Principal).Single().Id)).Should().BeNull("D5");
        loan.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_IsSoft_AndReleasesEveryEntry()
    {
        var loan = WithEntries(1_000_000, repaid: 300_000);

        await _service.DeleteAsync(_userId, loan.Id);

        loan.DeletedAt.Should().NotBeNull();
        loan.Repayments.Should().OnlyContain(e => e.DeletedAt != null);
        _loans.Verify(r => r.DeleteAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>()), Times.Never, "Q4: không xoá cứng");
    }

    [Fact]
    public async Task WriteOff_ClosesWithoutMovingMoney()
    {
        var loan = WithEntries(1_000_000, repaid: 300_000);

        var dto = await _service.WriteOffAsync(_userId, loan.Id);

        (loan.Status, dto.RemainingCents).Should().Be((LoanStatus.WrittenOff, 700_000L));
        _tracked.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithANewTransaction_FromAWallet_IsSavedOnce()
    {
        var wallet = Wallet(5_000_000);

        var result = await _service.CreateAsync(_userId, new LoanInput(
            LoanDirection.Lend, "Xe", "FE", 2_000_000, 0, null, CreateTransaction: new NewLoanTransaction(wallet.Id, NinetyDaysIn)));

        var transaction = _tracked.Should().ContainSingle().Subject;
        (transaction.TransactionType, transaction.Description, transaction.AmountCents).Should().Be((TransactionType.Debit, "Cho vay", 2_000_000L));
        wallet.BalanceCents.Should().Be(3_000_000);
        result.Loan.StartDate.Should().Be(new DateOnly(2026, 11, 30));
        _loans.Verify(r => r.Track(It.IsAny<Loan>()), Times.Once);
        _loans.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_FromATransaction_WithADifferentAmount_AmountMismatch()
    {
        var transaction = Locked(Guid.NewGuid(), 1_500_000, TransactionType.Debit);

        var act = () => _service.CreateAsync(_userId, new LoanInput(LoanDirection.Lend, "Xe", "FE", 2_000_000, 0, null, TransactionId: transaction.Id));

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.AmountMismatch);
    }
}
