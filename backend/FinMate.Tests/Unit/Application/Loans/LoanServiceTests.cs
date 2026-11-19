using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Loans;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

public class LoanServiceTests
{
    private readonly Mock<ILoanRepository> _loans = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ICreateManualTransactionCommandHandler> _transactions = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly LoanService _service;

    public LoanServiceTests() => _service = new LoanService(_loans.Object, _categories.Object, _transactions.Object);

    private Loan Existing(LoanDirection direction, long principal, long paid = 0)
    {
        var loan = new Loan { Id = Guid.NewGuid(), UserId = _userId, Direction = direction, Title = "Xe", Counterparty = "FE", PrincipalCents = principal };
        if (paid > 0)
        {
            loan.Repayments.Add(new LoanRepayment { Id = Guid.NewGuid(), LoanId = loan.Id, AmountCents = paid });
        }

        _loans.Setup(r => r.GetOwnedAsync(loan.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(loan);
        return loan;
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
        var accountId = Guid.NewGuid();
        var loanCategory = new Category { Id = Guid.NewGuid(), Slug = Category.LoanSlug };
        _categories.Setup(c => c.GetSystemBySlugAsync(Category.LoanSlug, It.IsAny<CancellationToken>())).ReturnsAsync(loanCategory);
        CreateManualTransactionCommand? sent = null;
        var txId = Guid.NewGuid();
        _transactions.Setup(t => t.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateManualTransactionCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(() => new TransactionCreationResult(TransactionDtoWithId(txId), false));

        var dto = await _service.RepayAsync(_userId, loan.Id, 2_000_000, accountId, null);

        sent!.TransactionType.Should().Be(TransactionType.Debit);
        sent.CategoryId.Should().Be(loanCategory.Id);
        sent.FinancialAccountId.Should().Be(accountId);
        dto.PaidCents.Should().Be(20_000_000);
        dto.RemainingCents.Should().Be(10_000_000);
        _loans.Verify(r => r.AddRepaymentAsync(loan, It.Is<LoanRepayment>(p => p.Id == sent.ClientRequestId), It.IsAny<CancellationToken>()), Times.Once);
        loan.Repayments.Last().TransactionId.Should().Be(txId);
    }

    [Fact]
    public async Task Repay_Lend_IsCredit_AndWithoutWalletTouchesNoTransaction()
    {
        var loan = Existing(LoanDirection.Lend, 15_000_000);
        await _service.RepayAsync(_userId, loan.Id, 5_000_000, null, null);
        _transactions.Verify(t => t.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        _transactions.Setup(t => t.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TransactionCreationResult(TransactionDtoWithId(Guid.NewGuid()), false));
        await _service.RepayAsync(_userId, loan.Id, 1_000_000, Guid.NewGuid(), null);
        _transactions.Verify(t => t.HandleAsync(It.Is<CreateManualTransactionCommand>(c => c.TransactionType == TransactionType.Credit), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Repay_MoreThanRemaining_Rejected_NothingWritten()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000, paid: 900);
        var act = () => _service.RepayAsync(_userId, loan.Id, 200, Guid.NewGuid(), null);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.ErrorCode == LoanErrorCodes.Overpay);
        _transactions.Verify(t => t.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _loans.Verify(r => r.AddRepaymentAsync(It.IsAny<Loan>(), It.IsAny<LoanRepayment>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var act = () => _service.RepayAsync(_userId, Guid.NewGuid(), 100, null, null);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    private static TransactionDto TransactionDtoWithId(Guid id)
    {
        var dto = (TransactionDto)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TransactionDto));
        typeof(TransactionDto).GetProperty(nameof(TransactionDto.Id))!.GetSetMethod(true)!.Invoke(dto, [id]);
        return dto;
    }

    [Fact]
    public async Task Repay_WalletFails_RemovesRecordedRepayment_SoLedgerAndWalletStayInSync()
    {
        var loan = Existing(LoanDirection.Borrow, 1_000);
        _transactions.Setup(t => t.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("FinancialAccount", Guid.NewGuid()));

        var act = () => _service.RepayAsync(_userId, loan.Id, 100, Guid.NewGuid(), null);

        await act.Should().ThrowAsync<NotFoundException>();
        loan.Repayments.Should().BeEmpty();
        _loans.Verify(r => r.RemoveRepaymentAsync(loan, It.IsAny<LoanRepayment>(), It.IsAny<CancellationToken>()), Times.Once);
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
}
