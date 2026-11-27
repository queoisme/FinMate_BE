using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.ForecastConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.ForecastConfig;

public class ForecastConfigPatchAndDiffTests
{
    private static readonly ForecastConfigSections Defaults = ForecastConfigSections.Of(new SystemConfigDocument());

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Patch_ChangesOnlyTheSentFields()
    {
        var result = ForecastConfigPatch.Apply(Defaults, Json("""{"forecast":{"lowBalanceHorizonDays":21},"limits":{"alertT1Percent":75}}"""));

        result.Forecast.LowBalanceHorizonDays.Should().Be(21);
        result.Limits.AlertT1Percent.Should().Be(75);
        result.Limits.AlertT2Percent.Should().Be(90);
        result.Forecast.Should().BeEquivalentTo(Defaults.Forecast with { LowBalanceHorizonDays = 21 });
    }

    [Fact]
    public void Patch_ReplacesArraysWhole()
    {
        var result = ForecastConfigPatch.Apply(Defaults, Json("""{"forecast":{"holidays":[{"name":"Tết","start":"2027-02-05","end":"2027-02-10","multiplierPercent":150}]}}"""));

        result.Forecast.Holidays.Should().ContainSingle().Which.MultiplierPercent.Should().Be(150);
    }

    [Theory]
    [InlineData("""{"maintenance":{"enabled":true}}""", ForecastConfigErrorCodes.OutOfScope)]
    [InlineData("""{"refunds":{"matchWindowDays":10}}""", ForecastConfigErrorCodes.OutOfScope)]
    [InlineData("""{"forecast":{"lowBalanceHorizonDayz":21}}""", ForecastConfigErrorCodes.UnknownField)]
    [InlineData("""{"forecast":{"lowBalanceHorizonDays":null}}""", ForecastConfigErrorCodes.InvalidValue)]
    [InlineData("""{"forecast":{"lowBalanceHorizonDays":"hai mốt"}}""", ForecastConfigErrorCodes.InvalidValue)]
    [InlineData("""{"forecast":{"holidays":[{"name":"x","start":"2027-01-01","end":"2027-01-02","multiplierPercent":100,"extra":1}]}}""", ForecastConfigErrorCodes.InvalidValue)]
    [InlineData("""{"forecast":5}""", ForecastConfigErrorCodes.InvalidValue)]
    [InlineData("""[1]""", ForecastConfigErrorCodes.InvalidValue)]
    public void Patch_RefusesAnythingOutsideTheTwoSections_OrMisspelled(string changes, string code)
    {
        var act = () => ForecastConfigPatch.Apply(Defaults, Json(changes));

        act.Should().Throw<BadRequestException>().Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public void Diff_ListsEachChangedLeafWithBeforeAndAfter()
    {
        var after = Defaults with { Forecast = Defaults.Forecast with { LowBalanceHorizonDays = 21, MinHistoryDays = 20 }, Limits = new LimitSettings { AlertT1Percent = 75 } };

        var diff = ForecastConfigDiff.Compute(Defaults, after);

        diff.Select(d => d.Path).Should().BeEquivalentTo("forecast.minHistoryDays", "forecast.lowBalanceHorizonDays", "limits.alertT1Percent");
        var horizon = diff.Single(d => d.Path == "forecast.lowBalanceHorizonDays");
        horizon.Before!.GetValue<int>().Should().Be(14);
        horizon.After!.GetValue<int>().Should().Be(21);
        ForecastConfigDiff.Compute(Defaults, Defaults).Should().BeEmpty();
    }

    [Fact]
    public void Rebase_CarriesOnlyTheDraftsOwnChanges_OntoTheNewActive()
    {
        var sourceBase = Defaults;
        var source = Defaults with { Forecast = Defaults.Forecast with { LowBalanceHorizonDays = 21 } };
        var active = Defaults with { Forecast = Defaults.Forecast with { BillReminderDaysBefore = 5 } };

        var (sections, conflicts) = ForecastConfigDiff.Rebase(source, sourceBase, active);

        sections.Forecast.LowBalanceHorizonDays.Should().Be(21, "thay đổi của bản nháp được mang sang");
        sections.Forecast.BillReminderDaysBefore.Should().Be(5, "thay đổi Admin vừa lưu không bị đè lặng lẽ");
        conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Rebase_ReportsFieldsBothSidesChanged_AndKeepsTheDraftValue()
    {
        var sourceBase = Defaults;
        var source = Defaults with { Forecast = Defaults.Forecast with { LowBalanceHorizonDays = 21 } };
        var active = Defaults with { Forecast = Defaults.Forecast with { LowBalanceHorizonDays = 30 } };

        var (sections, conflicts) = ForecastConfigDiff.Rebase(source, sourceBase, active);

        sections.Forecast.LowBalanceHorizonDays.Should().Be(21);
        var conflict = conflicts.Should().ContainSingle().Subject;
        conflict.Path.Should().Be("forecast.lowBalanceHorizonDays");
        conflict.BaseValue!.GetValue<int>().Should().Be(14);
        conflict.ActiveValue!.GetValue<int>().Should().Be(30);
        conflict.DraftValue!.GetValue<int>().Should().Be(21);
    }

    [Fact]
    public void Sections_ApplyTo_ReplacesOnlyTheTwoSections()
    {
        var document = new SystemConfigDocument { Maintenance = new MaintenanceSettings { Enabled = true } };
        var sections = Defaults with { Limits = new LimitSettings { AlertT1Percent = 60 } };

        var full = sections.ApplyTo(document);

        full.Maintenance.Enabled.Should().BeTrue();
        full.Limits.AlertT1Percent.Should().Be(60);
        ForecastConfigSections.Deserialize(sections.Serialize()).Should().BeEquivalentTo(sections);
    }
}
