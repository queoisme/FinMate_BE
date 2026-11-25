using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>
/// Hẹn nhắc ngày mua (từ đề xuất "hoãn mua tới ngày" của agent): chỉ ngày tương lai trong 365 ngày, nhắc lúc 08:00 giờ Việt Nam đúng ngày đó, nội dung cố định không mang số tiền,
/// hẹn lại cùng ngày không tạo thêm.
/// </summary>
public class PurchaseReminderServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (PurchaseReminderService Service, Mock<IForecastAlertDispatcher> Dispatcher) Build(bool created = true)
    {
        var dispatcher = new Mock<IForecastAlertDispatcher>();
        dispatcher.Setup(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created ? new ForecastAlert() : null);
        return (new PurchaseReminderService(dispatcher.Object), dispatcher);
    }

    [Fact]
    public async Task SchedulesAtEightInTheMorningVietnamTimeOnTheChosenDayWithFixedTextAndNoAmount()
    {
        var (service, dispatcher) = Build();
        var date = VietnamTime.Today().AddDays(10);

        var result = await service.ScheduleAsync(UserId, date);

        result.Created.Should().BeTrue();
        result.RemindAt.Should().Be(new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(7)));
        dispatcher.Verify(d => d.RaiseAsync(
            It.Is<ForecastAlertRequest>(r =>
                r.UserId == UserId
                && r.Kind == ForecastAlertKind.PurchaseReminder
                && r.DedupKey == $"purchase_reminder:{date:yyyy-MM-dd}"
                && r.NotBefore == result.RemindAt
                && !r.Title.Any(char.IsDigit) && !r.Body.Any(char.IsDigit)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemindingTheSameDayAgainCreatesNothingNew()
    {
        var (service, _) = Build(created: false);

        var result = await service.ScheduleAsync(UserId, VietnamTime.Today().AddDays(3));

        result.Created.Should().BeFalse();
    }

    [Fact]
    public async Task TodayAndThePastAreRejected()
    {
        var (service, dispatcher) = Build();

        foreach (var date in new[] { VietnamTime.Today(), VietnamTime.Today().AddDays(-1) })
        {
            var act = () => service.ScheduleAsync(UserId, date);
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(PurchaseReminderErrorCodes.InvalidDate);
        }

        dispatcher.Verify(d => d.RaiseAsync(It.IsAny<ForecastAlertRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MoreThanAYearAheadIsRejectedButExactlyAYearIsAllowed()
    {
        var (service, _) = Build();

        var tooFar = () => service.ScheduleAsync(UserId, VietnamTime.Today().AddDays(PurchaseReminderService.MaxDaysAhead + 1));
        await tooFar.Should().ThrowAsync<BusinessRuleException>();

        var ok = await service.ScheduleAsync(UserId, VietnamTime.Today().AddDays(PurchaseReminderService.MaxDaysAhead));
        ok.Created.Should().BeTrue();
    }
}
