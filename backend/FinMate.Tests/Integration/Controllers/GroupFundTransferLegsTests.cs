using System.Net;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, S6: mọi chỗ đọc tác động số dư của transfer đều đúng với transfer gắn quỹ nhóm (không ví đích), cả hai hướng.
/// Dòng S6 #1 (xoá), #12 (xoá ví), #14–15 (ranh giới dự báo, độ chính xác), và thói quen chi không thấy chúng.
/// </summary>
[Collection("Integration")]
public class GroupFundTransferLegsTests : IClassFixture<AuthApiFactory>
{
    private const long Start = 5_000_000;
    private const long Amount = 500_000;

    private readonly AuthApiFactory _factory;
    private readonly RefundApi _api;

    public GroupFundTransferLegsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    private async Task<(RefundApi.Session S, Guid User, GroupFundSeed.Seeded Fund)> SeedAsync(GroupFundDirection direction)
    {
        var s = await _api.StartAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var user = await db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync();
        var fund = await GroupFundSeed.FundTransferAsync(db, user, s.Account, direction, Amount);
        return (s, user, fund);
    }

    [Theory]
    [InlineData(GroupFundDirection.Out, Start - Amount)]
    [InlineData(GroupFundDirection.In, Start + Amount)]
    public async Task DeletingAFundTransfer_UndoesExactlyItsOwnLeg(GroupFundDirection direction, long afterSeed)
    {
        var (s, _, fund) = await SeedAsync(direction);
        (await _api.BalanceAsync(s.Account)).Should().Be(afterSeed);

        (await _api.SendAsync(HttpMethod.Delete, $"/api/v1/transactions/{fund.TransactionId}", s.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _api.BalanceAsync(s.Account)).Should().Be(Start, "xoá trả lại đúng vế của nó, kể cả khoản rút (tiền vào)");
    }

    [Fact]
    public async Task AWalletWithAFundTransfer_CannotBeDeleted()
    {
        var (s, _, _) = await SeedAsync(GroupFundDirection.Out);

        var response = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/financial-accounts/{s.Account}", s.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RefundApi.ErrorAsync(response)).Should().Be("FINANCIAL_ACCOUNT_HAS_TRANSACTIONS", "transfer gắn quỹ không có ví đích nhưng vẫn là giao dịch của ví nguồn");
    }

    [Theory]
    [InlineData(GroupFundDirection.Out, TransactionType.Debit)]
    [InlineData(GroupFundDirection.In, TransactionType.Credit)]
    public async Task ForecastBoundary_SeesTheFundTransfer_InItsDirection_NamedAfterTheFund(GroupFundDirection direction, TransactionType expected)
    {
        var (s, user, fund) = await SeedAsync(direction);
        var today = VietnamTime.Today();
        using var scope = _factory.Services.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IForecastDataRepository>();

        var all = await BoundaryTransfers.AllAsync(data, user, new HashSet<Guid> { s.Account }, today.AddDays(-1), today, default);
        var outflows = await BoundaryTransfers.OutflowsAsync(data, user, new HashSet<Guid> { s.Account }, today.AddDays(-1), today, default);
        var habits = await data.GetTransactionsAsync(user, today.AddDays(-1), today);

        all.Should().ContainSingle(t => t.Id == fund.TransactionId).Which.Should().Match<ForecastTransaction>(
            t => t.Type == expected && t.AmountCents == Amount && t.AccountId == s.Account && t.MerchantName == "Tiền điện nước");
        outflows.Any(t => t.Id == fund.TransactionId).Should().Be(direction == GroupFundDirection.Out, "chỉ tiền ra vào bộ khoản định kỳ");
        habits.Should().NotContain(t => t.Id == fund.TransactionId, "BR-FUND-08: không vào thói quen chi hay thu nhập");
    }

    [Fact]
    public async Task ForecastBoundary_IgnoresAFundTransferOnAWalletThatIsNotCounted()
    {
        var (_, user, fund) = await SeedAsync(GroupFundDirection.Out);
        var today = VietnamTime.Today();
        using var scope = _factory.Services.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IForecastDataRepository>();

        var all = await BoundaryTransfers.AllAsync(data, user, new HashSet<Guid> { Guid.NewGuid() }, today.AddDays(-1), today, default);

        all.Should().NotContain(t => t.Id == fund.TransactionId);
    }
}
