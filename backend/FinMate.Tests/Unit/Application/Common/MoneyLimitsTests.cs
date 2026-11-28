using FinMate.Application.Budgets.Commands;
using FinMate.Application.Common;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Application.Groups.Bills;
using FinMate.Application.Groups.Funds;
using FinMate.Application.QrTransfers;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation.Results;
using Xunit;

namespace FinMate.Tests.Unit.Application.Common;

/// <summary>
/// Pentest 2026-11-28 (#18): <c>POST /transactions</c> nhận 9.007.199.254.740.991 đ. Mọi chỗ nhập tiền của người dùng phải chung một trần
/// (<see cref="MoneyLimits.MaxAmountCents"/> = 100 tỷ đ, cùng mức đã dùng cho hồ sơ, dự báo, định kỳ, vay), nếu không vài khoản cực lớn
/// cộng dồn là tràn <c>bigint</c> và làm sai số dư, dự báo, báo cáo của chính người đó.
/// </summary>
public class MoneyLimitsTests
{
    private static readonly Guid U = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public static TheoryData<string, string, Func<long, IReadOnlyList<ValidationFailure>>> Cases => new()
    {
        { "giao dịch thủ công", "AmountCents", a => new CreateManualTransactionCommandValidator().Validate(
            new CreateManualTransactionCommand(U, U, null, a, TransactionType.Debit, Now, null, null)).Errors },
        { "chuyển giữa hai ví", "AmountCents", a => new CreateTransferCommandValidator().Validate(
            new CreateTransferCommand(U, U, Guid.NewGuid(), a, Now, null)).Errors },
        { "sửa giao dịch", "AmountCents", a => new UpdateTransactionCommandValidator().Validate(
            new UpdateTransactionCommand(U, U, U, null, null, a, TransactionType.Debit, Now, null, null)).Errors },
        { "tạo ngân sách", "LimitCents", a => new CreateBudgetCommandValidator().Validate(new CreateBudgetCommand(U, null, a)).Errors },
        { "sửa hạn mức ngân sách", "LimitCents", a => new UpdateBudgetLimitCommandValidator().Validate(new UpdateBudgetLimitCommand(U, U, a)).Errors },
        { "số dư ban đầu của ví", "InitialBalanceCents", a => new CreateFinancialAccountCommandValidator().Validate(
            new CreateFinancialAccountCommand(U, "Ví", AccountType.Cash, null, a)).Errors },
        { "đóng góp mục tiêu", "AmountCents", a => new ContributeToGoalCommandValidator().Validate(new ContributeToGoalCommand(U, U, a, null, null)).Errors },
        { "tạo mục tiêu", "TargetCents", a => new CreateSavingGoalCommandValidator().Validate(
            new CreateSavingGoalCommand(U, "Mục tiêu", a, Now.AddYears(1))).Errors },
        { "sửa mục tiêu", "TargetCents", a => new UpdateSavingGoalCommandValidator().Validate(
            new UpdateSavingGoalCommand(U, U, "Mục tiêu", a, Now.AddYears(1))).Errors },
        { "chuyển khoản QR", "AmountCents", a => new CreateQrTransferCommandValidator().Validate(
            new CreateQrTransferCommand(U, "970436", "12345678", null, "970422", a, null, null)).Errors },
        { "hóa đơn nhóm", "AmountCents", a => new CreateGroupBillCommandValidator().Validate(
            new CreateGroupBillCommand(U, U, "Ăn tối", a, U, Now, default, [], null, null, null)).Errors },
        { "dòng quỹ nhóm", "AmountCents", a => new CreateGroupFundEntryCommandValidator().Validate(
            new CreateGroupFundEntryCommand(U, U, U, GroupFundEntryKind.Contribution, a, Now)).Errors },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCapIsAccepted_OneAboveIsRejected(string _, string property, Func<long, IReadOnlyList<ValidationFailure>> validate)
    {
        validate(MoneyLimits.MaxAmountCents).Should().NotContain(e => e.PropertyName == property);

        validate(MoneyLimits.MaxAmountCents + 1).Should().Contain(e => e.PropertyName == property);
        validate(9_007_199_254_740_991).Should().Contain(e => e.PropertyName == property); // đúng giá trị pentest đã tạo được
    }

    [Fact]
    public void TheCapMatchesTheOneAlreadyUsedForProfileForecastRecurringAndLoans()
        => MoneyLimits.MaxAmountCents.Should().Be(100_000_000_000);
}
