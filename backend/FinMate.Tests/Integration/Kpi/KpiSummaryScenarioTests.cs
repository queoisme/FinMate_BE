using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Kpi;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Kpi;

/// <summary>
/// Kịch bản §5 của Phase 94 trên một database mới (không dữ liệu của test khác), nên kiểm được cả ảnh chụp tính năng.
/// Dùng repository và service thật; quyền và cấu hình là stub (quyền qua HTTP có ở <see cref="OpsKpiControllerTests"/>).
/// </summary>
public class KpiSummaryScenarioTests : IClassFixture<ReceiptScanDbFixture>
{
    private static readonly DateOnly Day0 = new(2025, 1, 6);   // thứ Hai
    private readonly ReceiptScanDbFixture _fixture;

    public KpiSummaryScenarioTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class JsonCache : ICacheService
    {
        public Dictionary<string, string> Items { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            Items[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Items.Remove(key);
            return Task.CompletedTask;
        }
    }

    private async Task<(KpiSummaryService Service, FinMateDbContext Db, KpiScenario Scenario, JsonCache Cache)> ArrangeAsync(Mock<IOpsAccessService>? access = null)
    {
        var db = _fixture.NewContext(_fixture.ConnectionStringFor($"kpi_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        var scenario = new KpiScenario(Day0);
        await scenario.SeedAsync(db);

        access ??= new Mock<IOpsAccessService>();
        access.Setup(a => a.RequireAsync(It.IsAny<Guid>(), It.IsAny<OpsCapability>(), It.IsAny<CancellationToken>())).ReturnsAsync(new User());
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(0, new SystemConfigDocument(), null));
        var cache = new JsonCache();
        return (new KpiSummaryService(access.Object, new KpiRepository(db), config.Object, cache), db, scenario, cache);
    }

    [Fact]
    public async Task Weekly_EveryMetricMatchesTheHandCountedScenario()
    {
        var (service, db, s, _) = await ArrangeAsync();
        await using var _ = db;

        var summary = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        summary.Granularity.Should().Be("week");
        summary.MinCellSize.Should().Be(5);
        summary.Periods.Should().HaveCount(3).And.OnlyContain(p => !p.Partial);
        summary.Periods.Select(p => p.Signups).Should().Equal(KpiScenario.Expected.Signups);
        summary.Periods.Select(p => p.EmailVerified).Should().Equal(KpiScenario.Expected.EmailVerified);
        summary.Periods.Select(p => p.OnboardingCompleted).Should().Equal(KpiScenario.Expected.OnboardingCompleted);
        summary.Periods.Select(p => p.MethodChosen).Should().Equal(KpiScenario.Expected.MethodChosen);
        summary.Periods.Select(p => p.DeletionRequests).Should().Equal(KpiScenario.Expected.DeletionRequests);
        summary.Periods.Select(p => p.TransactingUsers).Should().Equal(KpiScenario.Expected.TransactingUsers);
        summary.Periods.Select(p => p.Transactions.Total).Should().Equal(KpiScenario.Expected.TransactionsTotal);
        summary.Periods.Select(p => p.Notifications.Received).Should().Equal(KpiScenario.Expected.NotificationsReceived);
        summary.Periods.Select(p => p.Subscriptions.PayingTotal).Should().Equal(KpiScenario.Expected.PayingTotal);
        summary.Periods.Select(p => p.Subscriptions.PaidOrders).Should().Equal(KpiScenario.Expected.PaidOrders);
        summary.Periods.Select(p => p.Subscriptions.RevenueCents).Should().Equal(KpiScenario.Expected.Revenue);
        summary.Periods.Select(p => p.Subscriptions.NeedsReview).Should().Equal(KpiScenario.Expected.NeedsReview);
    }

    [Fact]
    public async Task Weekly_SmallCellsAreMasked_ButSystemTotalsAreNot()
    {
        var (service, db, s, _) = await ArrangeAsync();
        await using var _ = db;

        var summary = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        var week1 = summary.Periods[0].Transactions.BySource;
        week1["manual"].Should().Be(KpiCount.Exact(5), "đủ ngưỡng thì giữ số chính xác");
        week1["voice"].Masked.Should().BeTrue();
        week1["receipt"].Masked.Should().BeTrue();
        week1["notification"].Should().Be(KpiCount.Exact(0));
        week1["support"].Should().Be(KpiCount.Exact(0));
        week1.Keys.Should().BeEquivalentTo("notification", "manual", "voice", "receipt", "support");

        var week2 = summary.Periods[1];
        week2.Subscriptions.PayingTotal.Should().Be(3, "tổng toàn hệ thống không che");
        week2.Subscriptions.PayingByPlan.Keys.Should().BeEquivalentTo(s.ProPlan, s.MaxPlan);
        week2.Subscriptions.PayingByPlan.Values.Should().OnlyContain(c => c.Masked);
        week2.Notifications.Should().Be(new KpiNotificationsDto(6, 1, 1, 2, 0.5));
    }

    [Fact]
    public async Task Daily_UsesVietnamDays_AndCountsEachDayOnItsOwn()
    {
        var (service, db, s, _) = await ArrangeAsync();
        await using var _ = db;

        var summary = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "day");

        summary.Periods.Should().HaveCount(21);
        summary.Periods.Sum(p => p.Signups).Should().Be(20, "hai tài khoản nội bộ không tính");
        summary.Periods[3].Signups.Should().Be(1, "u3 đăng ký 00:30 giờ VN, tức 17:30 UTC hôm trước, thuộc ngày VN thứ 3");
        summary.Periods[2].Signups.Should().Be(1);
        summary.Periods[3].Transactions.BySource["manual"].Masked.Should().BeTrue();
        summary.Periods[9].TransactingUsers.Should().Be(2);
        summary.Periods[10].TransactingUsers.Should().Be(0, "u6 chỉ có giao dịch từ thông báo");
        summary.Periods[16].Transactions.Total.Should().Be(1, "nháp và giao dịch đã xoá không tính");
        summary.Periods[11].Notifications.ConversionRate.Should().Be(0, "ngày 11 có một log lỗi, không thành giao dịch");
        summary.Periods[0].Notifications.ConversionRate.Should().BeNull("không có thông báo nào");
    }

    [Fact]
    public async Task Month_CountsTransactingUsersOncePerPeriod_AndMarksACutPeriodPartial()
    {
        var (service, db, s, _) = await ArrangeAsync();
        await using var _ = db;

        var summary = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "month");

        var month = summary.Periods.Should().ContainSingle().Subject;
        month.Partial.Should().BeTrue("khoảng 06–26/01 cắt tháng 1");
        month.Signups.Should().Be(20);
        month.TransactingUsers.Should().Be(6, "u1, u2, u3, u5, u8, u10 — u1 giao dịch bốn ngày vẫn chỉ là một người");
        month.Transactions.Total.Should().Be(13);
    }

    [Fact]
    public async Task FeatureUsers_IsACurrentSnapshot_WithoutInternalOrPendingDeletionAccounts()
    {
        var (service, db, s, _) = await ArrangeAsync();
        await using var _ = db;

        var summary = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        summary.FeatureUsers.Should().BeEquivalentTo(new { Budgets = 1L, Goals = 2L, Loans = 1L, Groups = 0L });
        summary.Notes.ManualIncludesChat.Should().BeTrue();
    }

    [Fact]
    public async Task SecondCall_IsServedFromTheCache_WithMasksIntact()
    {
        var (service, db, s, cache) = await ArrangeAsync();
        await using var _ = db;
        var first = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        await db.Database.ExecuteSqlRawAsync("DELETE FROM transactions");
        var second = await service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        cache.Items.Keys.Should().ContainSingle().Which.Should().Be($"kpi:summary:{s.Day0:yyyy-MM-dd}:{s.LastDay:yyyy-MM-dd}:Week:5");
        second.Periods[0].Transactions.Total.Should().Be(first.Periods[0].Transactions.Total);
        second.Periods[0].Transactions.BySource["voice"].Masked.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutViewProductKpi_NothingIsRead()
    {
        var access = new Mock<IOpsAccessService>();
        var (service, db, s, cache) = await ArrangeAsync(access);
        await using var _ = db;
        access.Setup(a => a.RequireAsync(It.IsAny<Guid>(), OpsCapability.ViewProductKpi, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền thực hiện thao tác này."));

        var act = () => service.GetSummaryAsync(Guid.NewGuid(), s.Day0, s.LastDay, "week");

        await act.Should().ThrowAsync<ForbiddenException>();
        cache.Items.Should().BeEmpty();
    }
}
