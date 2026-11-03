using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using static FinMate.Tests.Unit.Application.Forecasting.ForecastTestData;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class RecurringItemServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IRecurringItemRepository> _items = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly RecurringItemService _service;

    public RecurringItemServiceTests()
    {
        _service = new RecurringItemService(
            _items.Object, _categories.Object, _accounts.Object, _cache.Object,
            new CreateRecurringItemCommandValidator(), new UpdateRecurringItemCommandValidator());
    }

    private CreateRecurringItemCommand Create(string counterparty = "Netflix", Guid? categoryId = null)
        => new(_userId, RecurringDirection.Outflow, "Netflix", counterparty, 260_000, RecurringCadence.Monthly,
            FinMate.Application.Common.VietnamTime.Today().AddDays(5), categoryId, null);

    private RecurringItem Stored(RecurringStatus status)
    {
        var item = new RecurringItem { Id = Guid.NewGuid(), UserId = _userId, Name = "Netflix", CounterpartyKey = "netflix", Status = status, AmountCents = 260_000 };
        _items.Setup(r => r.GetAsync(item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        return item;
    }

    [Fact]
    public async Task Create_NewCounterparty_IsActiveAndManual()
    {
        var dto = await _service.CreateAsync(Create());

        dto.Status.Should().Be("active");
        dto.Source.Should().Be("manual");
        dto.CounterpartyKey.Should().Be("netflix");
        _items.Verify(r => r.AddAsync(It.IsAny<RecurringItem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_CounterpartyAlreadyTracked_IsAConflict()
    {
        _items.Setup(r => r.FindByCounterpartyAsync(_userId, RecurringDirection.Outflow, "netflix", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecurringItem { Status = RecurringStatus.Active });

        var act = () => _service.CreateAsync(Create());

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ForecastErrorCodes.RecurringDuplicate);
    }

    [Fact]
    public async Task Create_PreviouslyRejectedCounterparty_RevivesThatRow()
    {
        var rejected = new RecurringItem { Id = Guid.NewGuid(), UserId = _userId, Status = RecurringStatus.Rejected };
        _items.Setup(r => r.FindByCounterpartyAsync(_userId, RecurringDirection.Outflow, "netflix", It.IsAny<CancellationToken>())).ReturnsAsync(rejected);

        var dto = await _service.CreateAsync(Create());

        dto.Id.Should().Be(rejected.Id);
        dto.Status.Should().Be("active");
        _items.Verify(r => r.AddAsync(It.IsAny<RecurringItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SomeoneElsesCategory_IsRejected()
    {
        var categoryId = Guid.NewGuid();
        _categories.Setup(r => r.GetByIdAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = categoryId, IsSystem = false, UserId = Guid.NewGuid() });

        var act = () => _service.CreateAsync(Create(categoryId: categoryId));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ForecastErrorCodes.RecurringReferenceInvalid);
    }

    [Fact]
    public async Task Confirm_ASuggestion_ActivatesIt_AndClearsTheForecastCache()
    {
        var item = Stored(RecurringStatus.Suggested);

        (await _service.ConfirmAsync(_userId, item.Id)).Status.Should().Be("active");
        _cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains("forecast_baseline")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_AnActiveItemAgain_IsIdempotent()
    {
        var item = Stored(RecurringStatus.Active);

        (await _service.ConfirmAsync(_userId, item.Id)).Status.Should().Be("active");
    }

    [Fact]
    public async Task Reject_AnActiveItem_IsAStateConflict()
    {
        var item = Stored(RecurringStatus.Active);

        var act = () => _service.RejectAsync(_userId, item.Id);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ForecastErrorCodes.RecurringState);
    }

    [Fact]
    public async Task Cancel_KeepsTheRowSoItIsNotSuggestedAgain()
    {
        var item = Stored(RecurringStatus.Active);

        await _service.CancelAsync(_userId, item.Id);

        item.Status.Should().Be(RecurringStatus.Cancelled);
        item.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AnotherUsersItem_IsNotFound()
    {
        var act = () => _service.PauseAsync(_userId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
