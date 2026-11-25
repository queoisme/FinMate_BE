using System.Text.Json;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Forecasting;

/// <summary>
/// Nút hành động đi kèm cảnh báo dự báo: tính bằng code từ loại cảnh báo và payload đã lưu. Cảnh báo cũ thiếu trường thì không có nút thay vì đoán.
/// </summary>
public class ForecastAlertActionsTests
{
    private static JsonElement Data(object data) => JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement.Clone();

    [Fact]
    public void ABudgetAtRiskOffersRaisingTheLimitToTheProjectionRoundedUpAndOpeningTheForecast()
    {
        var actions = ForecastAlertActions.For(
            ForecastAlertKind.BudgetRisk,
            Data(new { budgetId = Guid.NewGuid().ToString(), categoryId = "cat-1", limitCents = "3000000", projectedCents = "3412000" }));

        actions.Select(a => a.Kind).Should().Equal(ForecastAlertActionKinds.RaiseBudgetLimit, ForecastAlertActionKinds.OpenForecast);
        actions[0].AmountCents.Should().Be(3_420_000, "làm tròn LÊN chục nghìn đồng để đủ chứa mức dự kiến");
        actions[0].CategoryId.Should().Be("cat-1");
        actions[0].Title.Should().Contain("3.420.000");
    }

    [Fact]
    public void ATotalBudgetHasAnEmptyCategoryIdAndStillGetsTheRaiseAction()
    {
        var actions = ForecastAlertActions.For(ForecastAlertKind.BudgetRisk, Data(new { categoryId = "", limitCents = "10000000", projectedCents = "12000000" }));

        actions[0].Kind.Should().Be(ForecastAlertActionKinds.RaiseBudgetLimit);
        actions[0].CategoryId.Should().BeEmpty();
        actions[0].AmountCents.Should().Be(12_000_000);
    }

    [Fact]
    public void AnOldBudgetAlertWithoutTheCategoryFieldOnlyOffersToOpenTheForecast()
    {
        var actions = ForecastAlertActions.For(ForecastAlertKind.BudgetRisk, Data(new { budgetId = Guid.NewGuid().ToString(), projectedCents = "3412000" }));

        actions.Select(a => a.Kind).Should().Equal(ForecastAlertActionKinds.OpenForecast);
    }

    [Fact]
    public void NoRaiseIsOfferedWhenTheRoundedProjectionIsNotAboveTheCurrentLimit()
    {
        var actions = ForecastAlertActions.For(ForecastAlertKind.BudgetRisk, Data(new { categoryId = "c", limitCents = "3420000", projectedCents = "3412000" }));

        actions.Select(a => a.Kind).Should().Equal(ForecastAlertActionKinds.OpenForecast);
    }

    [Fact]
    public void ALowBalanceAlertOffersTheCashflowForecast()
    {
        var actions = ForecastAlertActions.For(ForecastAlertKind.LowBalance, Data(new { shortfallDate = "2026-11-20" }));

        actions.Should().ContainSingle().Which.Kind.Should().Be(ForecastAlertActionKinds.OpenForecast);
    }

    [Fact]
    public void ABillStatusCheckOffersToTrackTheItemAgain()
    {
        var itemId = Guid.NewGuid().ToString();

        var actions = ForecastAlertActions.For(ForecastAlertKind.BillStatusCheck, Data(new { recurringItemId = itemId, dueDate = "2026-11-01" }));

        var resume = actions.Should().ContainSingle().Subject;
        resume.Kind.Should().Be(ForecastAlertActionKinds.ResumeRecurring);
        resume.TargetId.Should().Be(itemId);
    }

    [Theory]
    [InlineData(ForecastAlertKind.Anomaly)]
    [InlineData(ForecastAlertKind.BillDue)]
    [InlineData(ForecastAlertKind.PurchaseReminder)]
    [InlineData(ForecastAlertKind.ListenerHealth)]
    public void OtherKindsHaveNoActions(ForecastAlertKind kind)
    {
        ForecastAlertActions.For(kind, Data(new { recurringItemId = Guid.NewGuid().ToString(), categoryId = "c", projectedCents = "5000000" })).Should().BeEmpty();
    }

    [Fact]
    public void EmptyOrMalformedPayloadNeverThrows()
    {
        foreach (var kind in Enum.GetValues<ForecastAlertKind>())
        {
            ForecastAlertActions.For(kind, default).Should().NotBeNull();
            ForecastAlertActions.For(kind, Data(new { projectedCents = "không phải số", categoryId = "c" })).Should().NotBeNull();
        }
    }
}
