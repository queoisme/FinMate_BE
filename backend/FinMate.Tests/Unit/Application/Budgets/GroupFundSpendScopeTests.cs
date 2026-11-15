using FinMate.Application.Budgets;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Budgets;

/// <summary>
/// Phase 85, D13: khoản chi từ quỹ khớp ngân sách nhóm theo ĐÚNG luật của giao dịch đã chia sẻ. Cùng một bộ ca chạy qua cả
/// <see cref="BudgetSpendScope.Group"/> và <see cref="BudgetSpendScope.GroupFund"/>; hai biểu thức lệch nhau là test đỏ.
/// </summary>
public class GroupFundSpendScopeTests
{
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid Electricity = Guid.NewGuid();   // danh mục hệ thống
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Utilities = Guid.NewGuid();     // danh mục nhóm "Điện nước"
    private static readonly DateTimeOffset From = new(2026, 10, 31, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 30, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Inside = new(2026, 11, 14, 5, 0, 0, TimeSpan.Zero);

    public static TheoryData<string?, string?, string?, string?, DateTimeOffset, bool> Cases()
    {
        var data = new TheoryData<string?, string?, string?, string?, DateTimeOffset, bool>();

        // (danh mục hệ thống của khoản, danh mục nhóm của khoản, ngân sách theo hệ thống, ngân sách theo nhóm, thời điểm, khớp?)
        data.Add("electricity", null, null, null, Inside, true);              // ngân sách tổng đếm mọi khoản
        data.Add("electricity", null, "electricity", null, Inside, true);
        data.Add("food", null, "electricity", null, Inside, false);
        data.Add("electricity", "utilities", "electricity", null, Inside, false); // đã có danh mục nhóm: ngân sách hệ thống không đếm
        data.Add("electricity", "utilities", null, "utilities", Inside, true);   // danh mục nhóm thắng
        data.Add(null, "utilities", null, "utilities", Inside, true);
        data.Add("electricity", null, null, "utilities", Inside, false);
        data.Add("electricity", null, null, null, To, false);                    // ngoài kỳ
        data.Add("electricity", null, null, null, From.AddTicks(-1), false);
        return data;
    }

    private static Guid? Id(string? name) => name switch
    {
        "electricity" => Electricity,
        "food" => Food,
        "utilities" => Utilities,
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SharedTransactions_AndFundExpenses_MatchTheSameBudgets(
        string? category, string? groupCategory, string? budgetCategory, string? budgetGroupCategory, DateTimeOffset at, bool expected)
    {
        var transaction = new Transaction
        {
            GroupId = GroupId, CategoryId = Id(category), GroupCategoryId = Id(groupCategory), TransactedAt = at,
            TransactionType = TransactionType.Debit, Status = TransactionStatus.Confirmed, AmountCents = 800_000,
        };
        var entry = new GroupFundEntry
        {
            GroupId = GroupId, CategoryId = Id(category), GroupCategoryId = Id(groupCategory), TransactedAt = at,
            Kind = GroupFundEntryKind.Expense, AmountCents = 800_000,
        };

        var scope = BudgetSpendScope.GroupAll(GroupId, Id(budgetCategory), Id(budgetGroupCategory), From, To);

        scope.Shared.Compile()(transaction).Should().Be(expected, "giao dịch chia sẻ");
        scope.Fund.Compile()(entry).Should().Be(expected, "khoản chi từ quỹ phải theo cùng luật");
    }

    [Theory]
    [InlineData(GroupFundEntryKind.Contribution)]
    [InlineData(GroupFundEntryKind.Withdrawal)]
    [InlineData(GroupFundEntryKind.Adjustment)]
    public void OnlyExpenses_CountTowardsAGroupBudget(GroupFundEntryKind kind)
    {
        var entry = new GroupFundEntry { GroupId = GroupId, Kind = kind, AmountCents = 500_000, TransactedAt = Inside };

        BudgetSpendScope.ContributionOf(entry, BudgetSpendScope.GroupFund(GroupId, null, null, From, To)).Should().Be(0, "BR-FUND-11: góp không là chi ở đâu cả");
    }

    [Fact]
    public void ADeletedExpense_OrAnotherGroupsExpense_DoesNotCount()
    {
        var scope = BudgetSpendScope.GroupFund(GroupId, null, null, From, To);
        var deleted = new GroupFundEntry { GroupId = GroupId, Kind = GroupFundEntryKind.Expense, AmountCents = 1, TransactedAt = Inside, DeletedAt = Inside };
        var elsewhere = new GroupFundEntry { GroupId = Guid.NewGuid(), Kind = GroupFundEntryKind.Expense, AmountCents = 1, TransactedAt = Inside };
        var counted = new GroupFundEntry { GroupId = GroupId, Kind = GroupFundEntryKind.Expense, AmountCents = 800_000, TransactedAt = Inside };

        BudgetSpendScope.ContributionOf(deleted, scope).Should().Be(0);
        BudgetSpendScope.ContributionOf(elsewhere, scope).Should().Be(0);
        BudgetSpendScope.ContributionOf(counted, scope).Should().Be(800_000);
    }
}
