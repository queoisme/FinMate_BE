using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 92 (BR-INS-03): liên kết kỳ đã trả khi gộp, xoá, đổi chiều tiền.</summary>
public class RecurringPaymentSyncTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IRecurringItemRepository> _items = new();
    private readonly RecurringItem _item;
    private readonly RecurringItemPayment _payment;
    private readonly Transaction _loser;
    private readonly Transaction _survivor;

    public RecurringPaymentSyncTests()
    {
        _item = new RecurringItem
        {
            Id = Guid.NewGuid(), UserId = _userId, Direction = RecurringDirection.Outflow, CounterpartyKey = "fe credit", AmountCents = 1_200_000,
            Cadence = RecurringCadence.Monthly, AnchorDay = 15, NextDueDate = new DateOnly(2027, 1, 15), TotalOccurrences = 12, PrepaidOccurrences = 11,
            LivePaymentCount = 1, Status = RecurringStatus.Completed, CompletedAt = Now,
        };
        _loser = new Transaction { Id = Guid.NewGuid(), UserId = _userId, TransactionType = TransactionType.Debit };
        _survivor = new Transaction { Id = Guid.NewGuid(), UserId = _userId, TransactionType = TransactionType.Debit };
        _payment = new RecurringItemPayment
        {
            Id = Guid.NewGuid(), UserId = _userId, RecurringItemId = _item.Id, TransactionId = _loser.Id, DueDate = new DateOnly(2026, 12, 15),
            Kind = RecurringPaymentKind.Matched,
        };
        _items.Setup(r => r.FindLivePaymentByTransactionAsync(_userId, _loser.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_payment);
        _items.Setup(r => r.GetAsync(_item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(_item);
    }

    private RecurringPaymentSync Sync() => new(_items.Object);

    [Fact]
    public async Task Merge_MovesThePaymentToTheSurvivor_CountUnchanged()
    {
        await Sync().OnMergedAsync(_userId, _loser, _survivor, Now);

        _payment.TransactionId.Should().Be(_survivor.Id);
        _payment.DeletedAt.Should().BeNull();
        _item.Status.Should().Be(RecurringStatus.Completed);
        _item.PaidOccurrences.Should().Be(12);
    }

    [Fact]
    public async Task Merge_IntoASurvivorThatAlreadyPaysAPeriod_LosesOneInstallment()
    {
        _items.Setup(r => r.FindLivePaymentByTransactionAsync(_userId, _survivor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new RecurringItemPayment());

        await Sync().OnMergedAsync(_userId, _loser, _survivor, Now);

        _payment.DeletedAt.Should().Be(Now);
        _item.Status.Should().Be(RecurringStatus.Active);
        _item.PaidOccurrences.Should().Be(11);
        _item.NextDueDate.Should().Be(new DateOnly(2026, 12, 15), "D4: lùi đúng một kỳ");
    }

    [Fact]
    public async Task Merge_IntoATransfer_LosesTheInstallment()
    {
        _survivor.TransactionType = TransactionType.Transfer;

        await Sync().OnMergedAsync(_userId, _loser, _survivor, Now);

        _payment.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Removal_ReleasesAndReopens()
    {
        await Sync().OnTransactionRemovedAsync(_userId, _loser.Id, Now);

        _payment.DeletedAt.Should().Be(Now);
        _item.Status.Should().Be(RecurringStatus.Active);
        _item.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task SameDirectionEdit_KeepsThePayment()
    {
        await Sync().OnTypeChangingAsync(_userId, _loser, TransactionType.Debit, Now);
        await Sync().OnTypeChangingAsync(_userId, _loser, null, Now);

        _payment.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task SettlementRemoval_DoesNotRewindTheDueDate()
    {
        _payment.Kind = RecurringPaymentKind.Settlement;

        await Sync().OnTransactionRemovedAsync(_userId, _loser.Id, Now);

        _item.NextDueDate.Should().Be(new DateOnly(2027, 1, 15));
    }
}
