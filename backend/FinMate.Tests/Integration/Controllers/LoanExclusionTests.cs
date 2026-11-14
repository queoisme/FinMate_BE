using System.Net.Http.Json;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, Task 0 (S2): cho vay/mượn không phải chi tiêu ở MỌI nơi. <c>FinanceSnapshotRepository</c> từng thiếu điều kiện này, nên khoản cho
/// vay bị trừ vào <c>/me/spendable</c> và vào "kế hoạch tháng" mà công cụ <c>simulate_purchase</c> của Agent đọc qua <c>/forecast/purchase-check</c>.
/// </summary>
[Collection("Integration")]
public class LoanExclusionTests : IClassFixture<AuthApiFactory>
{
    private const long Income = 20_000_000;
    private const long Food = 300_000;
    private const long Loan = 2_000_000;

    private readonly RefundApi _api;

    public LoanExclusionTests(AuthApiFactory factory)
    {
        _api = new RefundApi(factory);
    }

    private record Spendable(long? IncomeCents, long SpentCents, long? SpendableCents);
    private record PlanImpact(string Status, long? SpendableCents, long? SpendableAfterCents);
    private record PurchaseCheck(PlanImpact Plan);

    private async Task<RefundApi.Session> SeedAsync()
    {
        var s = await _api.StartAsync();
        (await _api.SendAsync(HttpMethod.Patch, "/api/v1/users/me", s.Token, new { displayName = "Loan User", monthlyIncomeCents = Income }))
            .EnsureSuccessStatusCode();
        var loan = await _api.WithDbAsync(db => db.Categories.Where(c => c.Slug == Category.LoanSlug && c.UserId == null).Select(c => c.Id).SingleAsync());
        await _api.SpendAsync(s, Food, category: s.Food, merchant: "Cơm trưa");
        await _api.SpendAsync(s, Loan, category: loan, merchant: "Cho bạn mượn");
        return s;
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<T>>(RefundApi.JsonOptions))!.Data;
    }

    [Fact]
    public async Task MeSpendable_DoesNotCountALoanAsSpending()
    {
        var s = await SeedAsync();

        var spendable = await ReadAsync<Spendable>(await _api.SendAsync(HttpMethod.Get, "/api/v1/me/spendable", s.Token));

        spendable.SpentCents.Should().Be(Food, "khoản cho mượn 2tr sẽ về lại, không phải chi tiêu");
        spendable.SpendableCents.Should().Be(Income - Food);
    }

    [Fact]
    public async Task AgentPurchaseCheck_PlanImpact_DoesNotCountALoanAsSpending()
    {
        var s = await SeedAsync();

        var check = await ReadAsync<PurchaseCheck>(
            await _api.SendAsync(HttpMethod.Post, "/api/v1/forecast/purchase-check", s.Token, new { amountCents = 100_000L }));

        check.Plan.SpendableCents.Should().Be(Income - Food, "simulate_purchase đọc cùng kế hoạch tháng với /me/spendable");
        check.Plan.SpendableAfterCents.Should().Be(Income - Food - 100_000);
    }
}
