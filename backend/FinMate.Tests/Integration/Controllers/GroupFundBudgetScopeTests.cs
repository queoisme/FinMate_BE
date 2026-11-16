using System.Net;
using System.Net.Http.Json;
using FinMate.Application.Budgets;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, D13: một định nghĩa chi tiêu nhóm cho cả giao dịch đã chia sẻ và khoản chi từ quỹ, trên Postgres thật. Tạo ngân sách nhóm thì phần chi
/// từ quỹ đã có được tính lùi vào; góp, dòng đã xoá và khoản chi của nhóm khác không được tính.
/// </summary>
[Collection("Integration")]
public class GroupFundBudgetScopeTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public GroupFundBudgetScopeTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private record BudgetData(Guid Id, long SpentCents, long LimitCents);

    [Fact]
    public async Task CreatingAGroupBudget_BackfillsFundExpenses_Only()
    {
        var s = await _api.StartAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var user = await db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync();
        var bills = await db.Categories.Where(c => c.Slug == "bills" && c.UserId == null).Select(c => c.Id).SingleAsync();

        // Góp 500k (không phải chi), chi 800k "Hoá đơn", một khoản chi đã xoá, một khoản chi danh mục khác.
        var fund = await GroupFundSeed.FundTransferAsync(db, user, s.Account, GroupFundDirection.Out);
        await GroupFundSeed.ExpenseAsync(db, fund.GroupId, fund.FundId, user, 800_000, bills);
        await GroupFundSeed.ExpenseAsync(db, fund.GroupId, fund.FundId, user, 300_000, bills, deletedAt: DateTimeOffset.UtcNow);
        await GroupFundSeed.ExpenseAsync(db, fund.GroupId, fund.FundId, user, 200_000, s.Food);

        var response = await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{fund.GroupId}/budgets", s.Token, new { categoryId = bills, limitCents = 1_000_000L });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var budget = (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<BudgetData>>(RefundApi.JsonOptions))!.Data;

        budget.SpentCents.Should().Be(800_000, "chỉ khoản chi còn sống, đúng danh mục; góp quỹ không phải chi");
    }

    [Fact]
    public async Task SumGroupNetSpend_AddsSharedPurchases_AndFundExpenses()
    {
        var s = await _api.StartAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var user = await db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync();
        var fund = await GroupFundSeed.FundTransferAsync(db, user, s.Account, GroupFundDirection.Out);
        await GroupFundSeed.ExpenseAsync(db, fund.GroupId, fund.FundId, user, 800_000, s.Food);
        var shared = await _api.SpendAsync(s, 150_000, category: s.Food, merchant: "Chợ");
        (await _api.SendAsync(HttpMethod.Put, $"/api/v1/transactions/{shared.Id}/group", s.Token, new { groupId = fund.GroupId })).EnsureSuccessStatusCode();
        var (start, end) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        var finance = scope.ServiceProvider.GetRequiredService<IGroupFinanceRepository>();

        var total = await finance.SumGroupNetSpendAsync(BudgetSpendScope.GroupAll(fund.GroupId, null, null, start, end), [], []);
        var withoutPending = await finance.SumGroupNetSpendAsync(BudgetSpendScope.GroupAll(fund.GroupId, null, null, start, end), [shared.Id], []);

        total.Should().Be(950_000);
        withoutPending.Should().Be(800_000, "giao dịch đang sửa trong bộ nhớ bị loại, caller tự cộng phần của nó");
    }
}
