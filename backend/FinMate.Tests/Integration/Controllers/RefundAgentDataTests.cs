using FinMate.Application.Agent;
using FinMate.Application.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 81, Task 12: công cụ của AI Agent (tổng chi/thu, lập ngân sách, runway, thử mua hàng) đọc qua <see cref="RepositoryFinancialDataSource"/>.
/// Thu nhập không gồm khoản hoàn, chi là số ròng, cặp đảo không có mặt, không tháng nào chi âm.
/// </summary>
[Collection("Integration")]
public class RefundAgentDataTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public RefundAgentDataTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    [Fact]
    public async Task MonthlySeriesAndCategories_SeeNetSpendAndIncomeWithoutRefunds()
    {
        var s = await _api.StartAsync();
        var now = DateTimeOffset.UtcNow;
        var (year, month) = VietnamTime.YearMonthOf(now);
        var thisMonthFirst = new DateOnly(year, month, 1);
        var twoMonthsAgo = new DateTimeOffset(thisMonthFirst.AddMonths(-2).ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero);
        var lastMonth = new DateTimeOffset(thisMonthFirst.AddMonths(-1).ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero);

        // Hai tháng trước: giày 900k, lương 10tr, khoản 500k bị đảo.
        var shoes = await _api.SpendAsync(s, 900_000, twoMonthsAgo);
        await _api.EarnAsync(s, 10_000_000, twoMonthsAgo, "Lương");
        var wrong = await _api.SpendAsync(s, 500_000, twoMonthsAgo, merchant: "Trừ nhầm");
        await _api.RefundAsync(s, wrong.Id, new { relation = "Reversal", transactedAt = twoMonthsAgo.AddHours(1) });

        // Tháng trước: chỉ có khoản hoàn 300k cho đôi giày → chi 0, không âm; thu vẫn 0.
        await _api.RefundAsync(s, shoes.Id, new { amountCents = 300_000, transactedAt = lastMonth });
        var userId = await _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());

        using var scope = _factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<RepositoryFinancialDataSource>();
        var series = await source.GetMonthlySeriesAsync(userId, 2, now);
        var categories = await source.GetCategorySpendAsync(userId, thisMonthFirst.AddMonths(-2), thisMonthFirst.AddDays(-1));

        series.IncomeVnd.Should().Equal(10_000_000, 0);
        series.SpendVnd.Should().Equal(900_000, 0);
        categories.Should().ContainSingle().Which.SpentVnd.Should().Be(600_000, "cả hai tháng gộp: 900k − 300k, cặp đảo không có mặt");
    }
}
