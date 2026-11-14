using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>Phase 85, S6: một chỗ duy nhất quyết định một giao dịch đổi số dư ví nào, bao nhiêu; gồm cả transfer gắn quỹ nhóm theo hai hướng.</summary>
public class TransferLegsTests
{
    private static readonly Guid Wallet = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static Transaction Tx(TransactionType type, Guid? counter = null, GroupFundDirection? direction = null) => new()
    {
        Id = Guid.NewGuid(),
        FinancialAccountId = Wallet,
        CounterAccountId = counter,
        AmountCents = 500_000,
        TransactionType = type,
        GroupFundEntryId = direction is null ? null : Guid.NewGuid(),
        GroupFundDirection = direction,
    };

    [Fact]
    public void Debit_TakesFromTheWallet()
        => TransferLegs.Of(Tx(TransactionType.Debit)).Should().Equal(new WalletLeg(Wallet, -500_000));

    [Fact]
    public void Credit_AddsToTheWallet()
        => TransferLegs.Of(Tx(TransactionType.Credit)).Should().Equal(new WalletLeg(Wallet, 500_000));

    [Fact]
    public void OrdinaryTransfer_MovesBetweenTwoWallets()
        => TransferLegs.Of(Tx(TransactionType.Transfer, Other)).Should().Equal(new WalletLeg(Wallet, -500_000), new WalletLeg(Other, 500_000));

    [Fact]
    public void FundTransferOut_TakesFromTheWalletOnly()
        => TransferLegs.Of(Tx(TransactionType.Transfer, direction: GroupFundDirection.Out)).Should().Equal(new WalletLeg(Wallet, -500_000));

    [Fact]
    public void FundTransferIn_AddsToTheWalletOnly()
        => TransferLegs.Of(Tx(TransactionType.Transfer, direction: GroupFundDirection.In)).Should().Equal(new WalletLeg(Wallet, 500_000));

    [Theory]
    [InlineData(TransactionType.Debit, null, MoneyDirection.Out)]
    [InlineData(TransactionType.Credit, null, MoneyDirection.In)]
    [InlineData(TransactionType.Transfer, GroupFundDirection.Out, MoneyDirection.Out)]
    [InlineData(TransactionType.Transfer, GroupFundDirection.In, MoneyDirection.In)]
    public void DirectionOf_FollowsTheWalletLeg(TransactionType type, GroupFundDirection? fund, MoneyDirection expected)
        => TransferLegs.DirectionOf(Tx(type, direction: fund)).Should().Be(expected);

    [Fact]
    public void DirectionOf_OrdinaryTransfer_IsAMove()
        => TransferLegs.DirectionOf(Tx(TransactionType.Transfer, Other)).Should().Be(MoneyDirection.Move);
}
