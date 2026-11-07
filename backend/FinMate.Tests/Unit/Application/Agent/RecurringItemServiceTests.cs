using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

public class RecurringItemServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid Me = Guid.NewGuid();

    private sealed class FakeItems : IRecurringItemRepository
    {
        public List<RecurringItem> All { get; } = [];

        public Task<List<RecurringItem>> GetListByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(All.Where(i => i.UserId == userId).ToList());

        public Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(All.Count(i => i.UserId == userId));

        public Task AddAsync(RecurringItem item, CancellationToken ct = default)
        {
            All.Add(item);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default) => Task.FromResult(All.RemoveAll(i => i.Id == id && i.UserId == userId) > 0);
    }

    private static Transaction Tx(string name, long amount, int month, int day, TransactionType type = TransactionType.Debit)
        => new() { Id = Guid.NewGuid(), UserId = Me, MerchantName = name, AmountCents = amount, TransactionType = type, Status = TransactionStatus.Confirmed, TransactedAt = new DateTimeOffset(2026, month, day, 9, 0, 0, TimeSpan.FromHours(7)) };

    private static (RecurringItemService Service, FakeItems Items) Build(params Transaction[] history)
    {
        var items = new FakeItems();
        var transactions = new Mock<ITransactionRepository>();
        transactions.Setup(t => t.GetConfirmedSinceAsync(Me, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(history.ToList());
        return (new RecurringItemService(items, transactions.Object), items);
    }

    private static readonly Transaction[] RentHistory = [Tx("Tiền nhà", 3_000_000, 8, 12), Tx("Tiền nhà", 3_000_000, 9, 12), Tx("Tiền nhà", 3_000_000, 10, 12)];

    [Fact]
    public async Task detected_items_are_only_suggestions_and_nothing_is_saved_until_the_user_confirms()
    {
        var (service, items) = Build(RentHistory);

        var listed = await service.ListAsync(Me, Now);

        listed.Items.Should().BeEmpty();
        listed.Suggestions.Should().ContainSingle().Which.Should().Be(new RecurringSuggestionDto("Tiền nhà", 3_000_000, "obligation", 12, 3));
        items.All.Should().BeEmpty();
    }

    [Fact]
    public async Task confirming_saves_the_item_and_stops_suggesting_it()
    {
        var (service, items) = Build(RentHistory);

        var created = await service.CreateAsync(Me, "  Tiền nhà ", 3_000_000, "Obligation", 12);
        var listed = await service.ListAsync(Me, Now);

        created.Should().Match<RecurringItemDto>(c => c.Name == "Tiền nhà" && c.Kind == "obligation");
        listed.Items.Should().ContainSingle();
        listed.Suggestions.Should().BeEmpty();
        items.All.Should().ContainSingle().Which.UserId.Should().Be(Me);
    }

    [Fact]
    public async Task entries_mark_stored_items_confirmed_and_candidates_estimated()
    {
        var (service, _) = Build([.. RentHistory, Tx("Gym", 500_000, 8, 5), Tx("Gym", 500_000, 9, 5), Tx("Gym", 500_000, 10, 5)]);
        await service.CreateAsync(Me, "Tiền nhà", 3_000_000, "obligation", 12);

        var entries = await service.GetEntriesAsync(Me, Now);

        entries.Single(e => e.Name == "Tiền nhà").Source.Should().Be(EvidenceSource.Confirmed);
        entries.Single(e => e.Name == "Gym").Source.Should().Be(EvidenceSource.Estimated);
    }

    [Fact]
    public async Task an_item_already_paid_this_month_is_flagged_so_it_is_not_deducted_twice()
    {
        var (service, _) = Build([.. RentHistory, Tx("Tiền nhà", 3_000_000, 11, 9)]); // đã trả tháng 11
        await service.CreateAsync(Me, "Tiền nhà", 3_000_000, "obligation", 12);

        (await service.GetEntriesAsync(Me, Now)).Single().PaidThisMonth.Should().BeTrue();
    }

    [Fact]
    public async Task a_payment_of_a_different_amount_or_name_does_not_count_as_paid()
    {
        var (service, _) = Build(Tx("Tiền nhà", 1_000_000, 11, 9), Tx("Tiền điện", 3_000_000, 11, 9));
        await service.CreateAsync(Me, "Tiền nhà", 3_000_000, "obligation", 12);

        (await service.GetEntriesAsync(Me, Now)).Single().PaidThisMonth.Should().BeFalse();
    }

    [Theory]
    [InlineData("", 1000, "obligation", 1, "invalid_recurring_name")]
    [InlineData("x", 0, "obligation", 1, "invalid_recurring_amount")]
    [InlineData("x", -5, "obligation", 1, "invalid_recurring_amount")]
    [InlineData("x", 1000, "spending", 1, "invalid_recurring_kind")]
    [InlineData("x", 1000, "obligation", 0, "invalid_recurring_day")]
    [InlineData("x", 1000, "obligation", 32, "invalid_recurring_day")]
    public async Task invalid_input_is_rejected_with_a_fixed_code(string name, long amount, string kind, int day, string code)
    {
        var (service, items) = Build();

        var act = () => service.CreateAsync(Me, name, amount, kind, day);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(code);
        items.All.Should().BeEmpty();
    }

    [Fact]
    public async Task the_number_of_saved_items_is_capped()
    {
        var (service, items) = Build();
        for (var i = 0; i < AgentLimits.MaxRecurringItems; i++)
        {
            items.All.Add(new RecurringItem { Id = Guid.NewGuid(), UserId = Me, Name = $"k{i}", AmountCents = 1000, DayOfMonth = 1 });
        }

        var act = () => service.CreateAsync(Me, "thêm", 1000, "obligation", 1);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("recurring_limit_reached");
    }

    [Fact]
    public async Task one_user_can_neither_see_nor_delete_anothers_item()
    {
        var (service, items) = Build();
        var theirs = new RecurringItem { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "của người khác", AmountCents = 1000, DayOfMonth = 1 };
        items.All.Add(theirs);

        (await service.ListAsync(Me, Now)).Items.Should().BeEmpty();
        var act = () => service.DeleteAsync(Me, theirs.Id);

        await act.Should().ThrowAsync<NotFoundException>();
        items.All.Should().ContainSingle();
    }
}
