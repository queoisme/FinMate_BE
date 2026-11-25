using FinMate.Application.Groups.Bills;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Groups;

public class GroupDebtMathTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void EqualSharesPutTheRemainderOnThePayer()
    {
        var shares = GroupDebtMath.EqualShares(100_000, [A, B, C], B);

        shares.Sum(s => s.ShareCents).Should().Be(100_000);
        shares.Single(s => s.UserId == A).ShareCents.Should().Be(33_333);
        shares.Single(s => s.UserId == B).ShareCents.Should().Be(33_334);
    }

    [Fact]
    public void EqualSharesFallBackToTheFirstPersonWhenThePayerDoesNotShare()
    {
        var shares = GroupDebtMath.EqualShares(10, [A, B, C], Guid.NewGuid());

        shares.Select(s => s.ShareCents).Should().Equal(4, 3, 3);
    }

    [Fact]
    public void NetAndMinimalTransfersSettleEveryone()
    {
        // A trả 300k chia đều cho A, B, C; B trả 90k chia cho B, C.
        var bills = new[]
        {
            Bill(A, 300_000, (A, 100_000), (B, 100_000), (C, 100_000)),
            Bill(B, 90_000, (B, 45_000), (C, 45_000)),
        };
        var settlements = new List<GroupSettlement>();

        var net = GroupDebtMath.Net(bills, settlements);
        net[A].Should().Be(200_000);
        net[B].Should().Be(-55_000);
        net[C].Should().Be(-145_000);

        var transfers = GroupDebtMath.MinimalTransfers(net);
        transfers.Should().BeEquivalentTo([new DebtTransfer(C, A, 145_000), new DebtTransfer(B, A, 55_000)]);

        settlements.Add(new GroupSettlement { FromUserId = B, ToUserId = A, AmountCents = 55_000 });
        GroupDebtMath.Net(bills, settlements)[B].Should().Be(0);
    }

    [Fact]
    public void FormerMembersAreKeptApartAndNeverTransferred()
    {
        var bill = Bill(null, 100, (A, 100));

        var net = GroupDebtMath.Net([bill], []);

        net[GroupDebtMath.Former].Should().Be(100);
        net[A].Should().Be(-100);
        GroupDebtMath.MinimalTransfers(net).Should().BeEmpty();
    }

    private static GroupBill Bill(Guid? paidBy, long amount, params (Guid UserId, long Share)[] splits)
        => new() { PaidByUserId = paidBy, AmountCents = amount, Splits = splits.Select(s => new GroupBillSplit { UserId = s.UserId, ShareCents = s.Share }).ToList() };

    [Theory]
    [InlineData(null, "2026-11-20", "2026-11-30")]
    [InlineData(5, "2026-11-20", "2026-12-05")]
    [InlineData(20, "2026-11-20", "2026-11-20")]
    [InlineData(28, "2026-01-31", "2026-02-28")]
    [InlineData(null, "2026-02-10", "2026-02-28")]
    public void NextClosingIsTheNearestDateNotBeforeToday(int? day, string today, string expected)
        => GroupSummaryService.NextClosing(day, DateOnly.Parse(today)).Should().Be(DateOnly.Parse(expected));
}
