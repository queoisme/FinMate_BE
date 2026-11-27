using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>Phase 97 (D9): báo cáo độ chính xác chỉ gộp bản chụp của MỘT phương pháp, để số cũ (trung vị) và số mới không bị trộn.</summary>
public class ForecastAccuracyReportTests
{
    private readonly Mock<IForecastAccuracyRepository> _repository = new();
    private readonly ForecastAccuracyService _service;

    public ForecastAccuracyReportTests()
    {
        _repository.Setup(r => r.ListFilledSinceAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repository.Setup(r => r.ListFeedbackSinceAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var access = new Mock<IOpsAccessService>();
        access.Setup(a => a.RequireAsync(It.IsAny<Guid>(), OpsCapability.ViewMonitoring, It.IsAny<CancellationToken>())).ReturnsAsync(new User());
        _service = new ForecastAccuracyService(
            Mock.Of<IReportRepository>(), Mock.Of<IForecastService>(), Mock.Of<ISystemConfigProvider>(), _repository.Object, Mock.Of<IForecastDataRepository>(),
            Mock.Of<IFinancialAccountRepository>(), access.Object, NullLogger<ForecastAccuracyService>.Instance);
    }

    [Fact]
    public void NewSnapshotsUseTheCurrentMethod_WhichIsNotTheOldMedianBand()
    {
        ForecastMethods.Current.Should().Be(ForecastMethods.MeanDowIncomeSumBand).And.NotBe(ForecastMethods.MeanDowIncome).And.NotBe(ForecastMethods.MedianBand);
        ForecastMethods.All.Should().Contain([ForecastMethods.MedianBand, ForecastMethods.MeanDowIncome, ForecastMethods.MeanDowIncomeSumBand]);
    }

    [Fact]
    public async Task TheReportDefaultsToTheCurrentMethod_AndSaysWhichOneItMeasured()
    {
        var report = await _service.GetReportAsync(Guid.NewGuid(), 30, null);

        report.Method.Should().Be(ForecastMethods.Current);
        _repository.Verify(r => r.ListFilledSinceAsync(It.IsAny<DateOnly>(), ForecastMethods.Current, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.CountPendingAsync(It.IsAny<DateOnly>(), ForecastMethods.Current, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheOldMethodCanStillBeRead()
    {
        var report = await _service.GetReportAsync(Guid.NewGuid(), 30, ForecastMethods.MedianBand);

        report.Method.Should().Be(ForecastMethods.MedianBand);
        _repository.Verify(r => r.ListFilledSinceAsync(It.IsAny<DateOnly>(), ForecastMethods.MedianBand, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUnknownMethod_IsRejected()
    {
        var act = () => _service.GetReportAsync(Guid.NewGuid(), 30, "made_up");

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
