using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 81: <c>POST /transactions/{id}/refunds</c> qua HTTP thật, Postgres thật.</summary>
[Collection("Integration")]
public class TransactionRefundsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TransactionRefundsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record IdBody(bool Success, IdData Data);
    private record IdData(Guid Id);
    private record CategoryListBody(bool Success, List<CategoryData> Data);
    private record CategoryData(Guid Id, string Slug);
    private record TransactionBody(bool Success, TransactionData Data);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    internal record TransactionData(
        Guid Id, long AmountCents, string TransactionType, string Status, Guid? CategoryId, Guid FinancialAccountId,
        string? Relation, Guid? RelatedTransactionId, bool IsReversed, long? RefundedCents, long? RemainingRefundableCents, DateTimeOffset TransactedAt);

    internal sealed record Session(string Token, Guid Account, Guid Shopping);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, password, "Refund Test User"));
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return request;
    }

    private async Task<Guid> CreateAccountAsync(string token, string name = "Ví tiền mặt", long balance = 5_000_000)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new CreateFinancialAccountRequest(name, AccountType.Cash, null, balance)));
        return (await response.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
    }

    private async Task<Session> StartAsync()
    {
        var token = await RegisterAndLoginAsync();
        var account = await CreateAccountAsync(token);
        var categories = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/categories", token));
        var shopping = (await categories.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions))!.Data.First(c => c.Slug == "shopping").Id;
        return new Session(token, account, shopping);
    }

    private async Task<TransactionData> SpendAsync(Session s, long amount, DateTimeOffset? at = null, Guid? category = null)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", s.Token,
            new CreateManualTransactionRequest(s.Account, category ?? s.Shopping, amount, TransactionType.Debit, at ?? DateTimeOffset.UtcNow, "Shopee - Giày", null)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data;
    }

    private Task<HttpResponseMessage> RefundAsync(Session s, Guid original, object? body = null)
        => _client.SendAsync(Request(HttpMethod.Post, $"/api/v1/transactions/{original}/refunds", s.Token, body ?? new { }));

    private static async Task<TransactionData> DataOf(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data;

    private static async Task<string> ErrorOf(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code;

    private async Task<long> BalanceAsync(Guid account)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.FinancialAccounts.Where(a => a.Id == account).Select(a => a.BalanceCents).SingleAsync();
    }

    private async Task<long> SpentAsync(Guid budget)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        return await db.BudgetPeriods.Where(p => p.BudgetId == budget).OrderByDescending(p => p.PeriodStart).Select(p => p.SpentCents).FirstAsync();
    }

    private async Task<Guid> BudgetAsync(Session s, long limit)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/budgets", s.Token, new CreateBudgetRequest(s.Shopping, limit)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdBody>(JsonOptions))!.Data.Id;
    }

    [Fact]
    public async Task DefaultRefund_ReturnsTheRemainder_IntoTheOriginalWallet_AsShoppingNotIncome()
    {
        var s = await StartAsync();
        var budget = await BudgetAsync(s, 1_000_000);
        var shoes = await SpendAsync(s, 900_000);
        var balanceAfterPurchase = await BalanceAsync(s.Account);

        var response = await RefundAsync(s, shoes.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var refund = await DataOf(response);
        refund.AmountCents.Should().Be(900_000);
        refund.TransactionType.Should().Be("Credit");
        refund.Relation.Should().Be("Refund");
        refund.RelatedTransactionId.Should().Be(shoes.Id);
        refund.CategoryId.Should().Be(s.Shopping, "BR-RF-03: danh mục của khoản gốc");
        refund.FinancialAccountId.Should().Be(s.Account);
        (await BalanceAsync(s.Account)).Should().Be(balanceAfterPurchase + 900_000);
        (await SpentAsync(budget)).Should().Be(0);
    }

    [Fact]
    public async Task PartialRefunds_AddUp_AndTheOneThatOverflowsIsRefused()
    {
        var s = await StartAsync();
        var budget = await BudgetAsync(s, 1_000_000);
        var shoes = await SpendAsync(s, 900_000);

        (await RefundAsync(s, shoes.Id, new { amountCents = 300_000 })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await RefundAsync(s, shoes.Id, new { amountCents = 500_000 })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SpentAsync(budget)).Should().Be(100_000);

        var overflow = await RefundAsync(s, shoes.Id, new { amountCents = 100_001 });
        overflow.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(overflow)).Should().Be("REFUND_EXCEEDS_ORIGINAL");

        var detail = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/transactions/{shoes.Id}", s.Token));
        var original = await DataOf(detail);
        original.RefundedCents.Should().Be(800_000);
        original.RemainingRefundableCents.Should().Be(100_000);
    }

    [Fact]
    public async Task Retrying_TheSameClientRequestId_Answers200_WithTheSameRefund()
    {
        var s = await StartAsync();
        var shoes = await SpendAsync(s, 900_000);
        var clientRequestId = Guid.NewGuid();

        var first = await RefundAsync(s, shoes.Id, new { amountCents = 300_000, clientRequestId });
        var again = await RefundAsync(s, shoes.Id, new { amountCents = 300_000, clientRequestId });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataOf(again)).Id.Should().Be((await DataOf(first)).Id);
        (await BalanceAsync(s.Account)).Should().Be(5_000_000 - 900_000 + 300_000);
    }

    [Fact]
    public async Task SomeoneElsesOriginal_OrWallet_Is404()
    {
        var owner = await StartAsync();
        var shoes = await SpendAsync(owner, 900_000);
        var stranger = await StartAsync();

        var foreignOriginal = await RefundAsync(stranger, shoes.Id);
        foreignOriginal.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignWallet = await RefundAsync(owner, shoes.Id, new { financialAccountId = stranger.Account });
        foreignWallet.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BalanceAsync(stranger.Account)).Should().Be(5_000_000);
    }

    [Fact]
    public async Task WithoutToken_Is401()
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/transactions/{Guid.NewGuid()}/refunds", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IneligibleOriginals_Are422()
    {
        var s = await StartAsync();
        var income = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", s.Token,
            new CreateManualTransactionRequest(s.Account, null, 10_000_000, TransactionType.Credit, DateTimeOffset.UtcNow, "Lương", null)));
        var salary = await DataOf(income);

        var refused = await RefundAsync(s, salary.Id);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(refused)).Should().Be("REFUND_ORIGINAL_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task Reversal_MustBeTheFullAmount_ThenMarksTheOriginal_AndExcludesBothFromTheBudget()
    {
        var s = await StartAsync();
        var budget = await BudgetAsync(s, 1_000_000);
        var wrong = await SpendAsync(s, 500_000);

        var partial = await RefundAsync(s, wrong.Id, new { relation = "Reversal", amountCents = 200_000 });
        partial.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(partial)).Should().Be("REVERSAL_AMOUNT_MISMATCH");

        var reversal = await RefundAsync(s, wrong.Id, new { relation = "Reversal" });
        reversal.StatusCode.Should().Be(HttpStatusCode.Created);
        (await DataOf(reversal)).AmountCents.Should().Be(500_000);

        var original = await DataOf(await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/transactions/{wrong.Id}", s.Token)));
        original.IsReversed.Should().BeTrue();
        original.RemainingRefundableCents.Should().Be(0);
        (await SpentAsync(budget)).Should().Be(0);
        (await BalanceAsync(s.Account)).Should().Be(5_000_000);

        var refundAfterReversal = await RefundAsync(s, wrong.Id, new { amountCents = 1 });
        (await ErrorOf(refundAfterReversal)).Should().Be("REFUND_ORIGINAL_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task OldOriginal_CanStillBeRefundedByHand()
    {
        // D12: cửa sổ 60 ngày chỉ giới hạn GỢI Ý; liên kết tay không giới hạn thời gian.
        var s = await StartAsync();
        var old = await SpendAsync(s, 900_000, DateTimeOffset.UtcNow.AddDays(-120));

        var response = await RefundAsync(s, old.Id, new { amountCents = 100_000 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ConcurrentRefunds_IntoTwoWallets_NeverExceedTheOriginal()
    {
        // BR-RF-02 dưới tải: 10 khoản hoàn 200k song song vào HAI ví (khoá xmin của ví không bắt được trường hợp này), gốc 900k.
        var s = await StartAsync();
        var second = await CreateAccountAsync(s.Token, "Ví 2", 0);
        var shoes = await SpendAsync(s, 900_000);
        var startFirst = await BalanceAsync(s.Account);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            RefundAsync(s, shoes.Id, new { amountCents = 200_000, financialAccountId = i % 2 == 0 ? s.Account : second })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(4);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var refunds = await db.Transactions.Where(t => t.RelatedTransactionId == shoes.Id).ToListAsync();
        refunds.Sum(r => r.AmountCents).Should().Be(800_000);
        (await BalanceAsync(s.Account) - startFirst + await BalanceAsync(second)).Should().Be(800_000, "số dư hai ví khớp đúng các khoản đã tạo");
    }
}
