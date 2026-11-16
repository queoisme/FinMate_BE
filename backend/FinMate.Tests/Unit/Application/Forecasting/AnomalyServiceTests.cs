using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

public class AnomalyServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();

    private readonly Mock<IAnomalyRepository> _anomalies = new();
    private readonly Mock<IForecastDataRepository> _data = new();
    private readonly Mock<IRecurringItemRepository> _items = new();
    private readonly Mock<IForecastSettingsService> _settings = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly AnomalyService _service;

    public AnomalyServiceTests()
    {
        _config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        _items.Setup(i => i.ListAsync(UserId, RecurringStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _anomalies.Setup(a => a.GetPriorNormalsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _anomalies.Setup(a => a.TryAddAsync(It.IsAny<TransactionAnomaly>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service = new AnomalyService(_anomalies.Object, _data.Object, _items.Object, _settings.Object, _config.Object);
    }

    private static ForecastTransaction Spend(int daysAgo, long amount, string merchant)
    {
        var date = VietnamTime.Today().AddDays(-daysAgo);
        return new ForecastTransaction(Guid.NewGuid(), Guid.NewGuid(), Food, amount, TransactionType.Debit,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), VietnamTime.Offset), date, merchant, CounterpartyKey.From(merchant), false);
    }

    private void History(params ForecastTransaction[] transactions)
        => _data.Setup(d => d.GetTransactionsAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

    private void Existing(params TransactionAnomaly[] anomalies)
        => _anomalies.Setup(a => a.GetForTransactionsAsync(UserId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anomalies);

    [Fact]
    public async Task Scan_FlagsANewSpike()
    {
        var big = Spend(0, 700_000, "Nhà hàng mới");
        History([.. Enumerable.Range(4, 6).Select(i => Spend(i, 100_000, $"Quán {i}")), big]);
        Existing();

        var found = await _service.ScanAsync(UserId);

        found.Should().ContainSingle().Which.Anomaly.TransactionId.Should().Be(big.Id);
        _anomalies.Verify(a => a.TryAddAsync(It.Is<TransactionAnomaly>(x => x.Severity == AnomalySeverity.High), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scan_ReturnsAnAlreadyFlaggedOpenAnomaly_WithoutFlaggingItAgain()
    {
        // Lượt trước đánh dấu xong rồi hỏng trước khi ghi cảnh báo: lượt này phải trả nó ra để bù cảnh báo.
        var big = Spend(0, 700_000, "Nhà hàng mới");
        History([.. Enumerable.Range(4, 6).Select(i => Spend(i, 100_000, $"Quán {i}")), big]);
        var flagged = new TransactionAnomaly { Id = Guid.NewGuid(), UserId = UserId, TransactionId = big.Id, Severity = AnomalySeverity.High };
        Existing(flagged);

        var found = await _service.ScanAsync(UserId);

        found.Should().ContainSingle().Which.Anomaly.Should().BeSameAs(flagged);
        _anomalies.Verify(a => a.TryAddAsync(It.IsAny<TransactionAnomaly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Scan_DoesNotReturnAnAnomalyTheUserAlreadyAnswered()
    {
        var big = Spend(0, 700_000, "Nhà hàng mới");
        History(big);
        Existing(new TransactionAnomaly { TransactionId = big.Id, Resolution = AnomalyResolution.Normal, ResolvedAt = DateTimeOffset.UtcNow });

        (await _service.ScanAsync(UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneOffFeedback_ExcludesTheTransactionFromForecasts()
    {
        var anomaly = new TransactionAnomaly { Id = Guid.NewGuid(), UserId = UserId, TransactionId = Guid.NewGuid() };
        _anomalies.Setup(a => a.GetAsync(anomaly.Id, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(anomaly);

        var result = await _service.SetFeedbackAsync(UserId, anomaly.Id, "one_off");

        result.Anomaly.Resolution.Should().Be("one_off");
        _settings.Verify(s => s.SetTransactionExclusionAsync(UserId, anomaly.TransactionId, true, ForecastExclusionReason.OneOff, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ChangingMindFromOneOff_PutsTheTransactionBack()
    {
        var anomaly = new TransactionAnomaly
        {
            Id = Guid.NewGuid(), UserId = UserId, TransactionId = Guid.NewGuid(), Resolution = AnomalyResolution.OneOff, ResolvedAt = DateTimeOffset.UtcNow,
        };
        _anomalies.Setup(a => a.GetAsync(anomaly.Id, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(anomaly);

        var result = await _service.SetFeedbackAsync(UserId, anomaly.Id, "suspicious");

        result.Guidance.Should().Be(AnomalyService.SuspiciousGuidance);
        _settings.Verify(s => s.SetTransactionExclusionAsync(UserId, anomaly.TransactionId, false, null, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task UnknownFeedback_IsRejected_AndSomeoneElsesAnomalyIsNotFound()
    {
        await _service.Invoking(s => s.SetFeedbackAsync(UserId, Guid.NewGuid(), "maybe")).Should().ThrowAsync<ValidationException>();
        await _service.Invoking(s => s.SetFeedbackAsync(UserId, Guid.NewGuid(), "normal")).Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(AnomalyResolution.Refunded, "refunded")]
    [InlineData(AnomalyResolution.Reversed, "reversed")]
    [InlineData(AnomalyResolution.Transfer, "transfer")]
    public async Task ListingShowsAnomaliesTheSystemClosed(AnomalyResolution resolution, string text)
    {
        // Lý do hệ thống tự đóng (Phase 81, 83) không có trong bảng phản hồi của client; trước đây liệt kê chúng ném InvalidOperationException.
        var closed = new TransactionAnomaly
        {
            Id = Guid.NewGuid(), UserId = UserId, TransactionId = Guid.NewGuid(), Kind = AnomalyKind.Duplicate, Severity = AnomalySeverity.High,
            AmountCents = 300_000, TypicalCents = 300_000, RatioPercent = 100, Resolution = resolution, ResolvedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _anomalies.Setup(a => a.ListAsync(UserId, false, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionAnomalyPage([closed], null));

        var page = await _service.ListAsync(UserId, openOnly: false, cursor: null, limit: 20);

        page.Items.Should().ContainSingle().Which.Resolution.Should().Be(text);
    }

    [Theory]
    [InlineData("refunded")]
    [InlineData("reversed")]
    [InlineData("transfer")]
    public async Task ClientsCannotSendASystemReason(string resolution)
    {
        var act = () => _service.SetFeedbackAsync(UserId, Guid.NewGuid(), resolution);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}

