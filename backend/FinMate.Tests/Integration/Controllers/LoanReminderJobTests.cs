using System.Net;
using FinMate.Application.Common;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Job nhắc hạn khoản vay (Phase 90, Task 10; chép từ bản sổ nợ Phase 86 cũ): BR-DEBT-14, D25, Q3 (push và hộp thư không có tên người hay số tiền), công tắc riêng.</summary>
[Collection("Integration")]
public class LoanReminderJobTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LoanReminderJobTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    private async Task<int> RunJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LoanReminderJob>().RunAtAsync(DateTimeOffset.UtcNow);
    }

    private static Task DebtAsync(LoanTestApi api, string name, string direction, long principal, DateOnly due)
        => api.ExpectAsync(HttpMethod.Post, "/api/v1/loans", new { direction, title = $"Cho {name} vay", counterparty = name, principalCents = principal, dueDate = due, startDate = due.AddDays(-40) }, HttpStatusCode.Created);

    [Fact]
    public async Task DueSoonAndOverdueLoans_AreRemindedOnce_WithoutNamesOrAmounts()
    {
        var api = await UserAsync();
        var today = VietnamTime.Today();
        await DebtAsync(api, "Lan Nguyễn", "lend", 1_000_000, today.AddDays(-1));
        await DebtAsync(api, "Chị Hoa", "borrow", 3_000_000, today.AddDays(2));
        await DebtAsync(api, "Xa lắm", "borrow", 3_000_000, today.AddDays(20));

        await RunJobAsync();
        await RunJobAsync();   // chạy lại trong ngày: không thêm

        var alerts = await api.InDbAsync(db => db.ForecastAlerts.AsNoTracking().Where(a => a.UserId == api.UserId && a.Kind == ForecastAlertKind.LoanDue).ToListAsync());
        alerts.Should().HaveCount(2);
        alerts.Select(a => a.DedupKey).Should().Contain(k => k.EndsWith(":overdue:0")).And.Contain(k => k.EndsWith(":before"));
        alerts.Select(a => a.Body).Should().BeEquivalentTo("Bạn có một khoản nợ đã quá hạn", "Bạn có một khoản nợ sắp đến hạn");
        foreach (var alert in alerts)
        {
            var text = alert.Title + " " + alert.Body + " " + alert.Data;
            text.Should().NotContain("Lan").And.NotContain("Hoa").And.NotContain("1000000").And.NotContain("3000000").And.NotContain("1.000.000").And.NotContain("3.000.000");
            var data = System.Text.Json.JsonDocument.Parse(alert.Data).RootElement;
            data.GetProperty("kind").GetString().Should().Be("loan_due");
            data.TryGetProperty("loanId", out _).Should().BeTrue();
            data.TryGetProperty("phase", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task TurningRemindersOff_StopsThem_IndependentlyOfForecastAlerts()
    {
        var api = await UserAsync();
        await DebtAsync(api, "Lan", "lend", 1_000_000, VietnamTime.Today());
        await api.ExpectAsync(HttpMethod.Patch, "/api/v1/users/me/notification-prefs", new { pushEnabled = true, budgetAlertsEnabled = true, loanRemindersEnabled = false }, HttpStatusCode.NoContent);

        await RunJobAsync();

        (await api.InDbAsync(db => db.ForecastAlerts.CountAsync(a => a.UserId == api.UserId && a.Kind == ForecastAlertKind.LoanDue))).Should().Be(0);
        (await api.ExpectAsync(HttpMethod.Get, "/api/v1/users/me", null, HttpStatusCode.OK)).GetProperty("loanRemindersEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SettledOrWrittenOffLoans_AreNotReminded()
    {
        var api = await UserAsync();
        var today = VietnamTime.Today();
        await DebtAsync(api, "Lan", "lend", 1_000_000, today.AddDays(-2));
        var id = (await api.ExpectAsync(HttpMethod.Get, "/api/v1/loans", null, HttpStatusCode.OK))[0].GetProperty("id").GetGuid();
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{id}/write-off", null, HttpStatusCode.OK);

        await RunJobAsync();

        (await api.InDbAsync(db => db.ForecastAlerts.CountAsync(a => a.UserId == api.UserId && a.Kind == ForecastAlertKind.LoanDue))).Should().Be(0);
    }
}
