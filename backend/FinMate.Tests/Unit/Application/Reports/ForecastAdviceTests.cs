using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Reports.Queries;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Reports;

/// <summary>
/// Phần "Hành động Đề xuất" của docx Flow 3 mục 4: không chỉ báo con số dự báo mà nói thẳng
/// mỗi ngày nên cắt bao nhiêu.
/// </summary>
public class ForecastAdviceTests
{
    private readonly Mock<ISpendingForecaster> _forecaster = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly GetSpendingForecastQueryHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();

    public ForecastAdviceTests()
    {
        _handler = new GetSpendingForecastQueryHandler(_forecaster.Object, _userRepository.Object);
    }

    private void Arrange(long projectedSpendCents, long? monthlyIncomeCents, int daysElapsed = 20)
    {
        _forecaster
            .Setup(f => f.ForecastCurrentMonthAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpendingForecastDto(
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(10),
                SpentSoFarCents: 6_000_000,
                ProjectedSpendCents: projectedSpendCents,
                DaysElapsed: daysElapsed,
                DaysInMonth: 30,
                BasedOnDays: 20,
                Confidence: "medium"));

        _userRepository
            .Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, MonthlyIncomeCents = monthlyIncomeCents });
    }

    [Fact]
    public async Task OverspendingProducesTheDailyCutFromTheDocument()
    {
        // Kịch bản nguyên văn docx: dự báo 10,2 triệu, thu nhập 9 triệu, bội chi 1,2 triệu.
        // Còn 10 ngày → cắt 120.000đ/ngày.
        Arrange(projectedSpendCents: 10_200_000, monthlyIncomeCents: 9_000_000);

        var forecast = await _handler.HandleAsync(new GetSpendingForecastQuery(_userId));

        forecast.Advice.Should().NotBeNull();
        forecast.Advice!.ProjectedBalanceCents.Should().Be(-1_200_000);
        forecast.Advice.SuggestedDailyCutCents.Should().Be(120_000);
    }

    [Fact]
    public async Task SpendingWithinIncomeNeedsNoCut()
    {
        Arrange(projectedSpendCents: 7_000_000, monthlyIncomeCents: 9_000_000);

        var advice = (await _handler.HandleAsync(new GetSpendingForecastQuery(_userId))).Advice;

        advice!.ProjectedBalanceCents.Should().Be(2_000_000);
        advice.SuggestedDailyCutCents.Should().BeNull();
    }

    [Fact]
    public async Task WithoutADeclaredIncomeThereIsNoAdviceAtAll()
    {
        // "Chưa biết" khác hẳn "không bội chi". Bịa ra một mức thu nhập để so là tệ hơn im lặng.
        Arrange(projectedSpendCents: 10_200_000, monthlyIncomeCents: null);

        var forecast = await _handler.HandleAsync(new GetSpendingForecastQuery(_userId));

        forecast.Advice.Should().BeNull();
        forecast.ProjectedSpendCents.Should().Be(10_200_000, "phần dự báo vẫn phải có");
    }

    [Fact]
    public async Task AZeroIncomeIsTreatedAsUndeclared()
    {
        Arrange(projectedSpendCents: 10_200_000, monthlyIncomeCents: 0);

        (await _handler.HandleAsync(new GetSpendingForecastQuery(_userId))).Advice.Should().BeNull();
    }

    [Fact]
    public async Task OnTheLastDayTheWholeOverspendLandsOnToday()
    {
        // Không còn ngày nào để chia — dồn hết vào hôm nay thay vì chia cho 0.
        Arrange(projectedSpendCents: 10_200_000, monthlyIncomeCents: 9_000_000, daysElapsed: 30);

        var advice = (await _handler.HandleAsync(new GetSpendingForecastQuery(_userId))).Advice;

        advice!.SuggestedDailyCutCents.Should().Be(1_200_000);
    }
}
