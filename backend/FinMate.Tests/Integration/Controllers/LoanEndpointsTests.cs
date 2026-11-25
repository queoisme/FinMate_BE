using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Endpoint của <c>/api/v1/loans</c> sau Phase 90 (Task 6): khoản của người khác là 404 ở mọi đường, gửi lại cùng <c>clientRequestId</c> không ghi
/// hai lần, hai lần trả đồng thời không cùng qua được kiểm "không trả quá", chi tiết / tóm tắt / lọc, giải ngân thêm, xoá dòng, xoá nợ, gắn giao dịch.
/// </summary>
[Collection("Integration")]
public class LoanEndpointsTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public LoanEndpointsTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthBody(AuthData Data);
    private record AuthData(string AccessToken);

    private async Task<string> LoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Loan Endpoints"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<AuthBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(string token, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length == 0)
        {
            return (response.StatusCode, default);
        }

        var root = JsonDocument.Parse(text).RootElement;
        return (response.StatusCode, response.IsSuccessStatusCode && root.TryGetProperty("data", out var d) ? d : root);
    }

    private async Task<Guid> WalletAsync(string token, long balance)
        => (await SendAsync(token, HttpMethod.Post, "/api/v1/financial-accounts", new { accountName = "Ví", accountType = "Cash", initialBalanceCents = balance }))
            .Body.GetProperty("id").GetGuid();

    private async Task<Guid> LoanAsync(string token, string direction = "lend", long principal = 1_000_000, object? extra = null)
    {
        var (status, body) = await SendAsync(token, HttpMethod.Post, "/api/v1/loans",
            new { direction, title = "Khoản", counterparty = "Người", principalCents = principal, dueDate = (DateOnly?)null });
        status.Should().Be(HttpStatusCode.Created, body.ToString());
        return body.GetProperty("id").GetGuid();
    }

    private async Task<long> BalanceAsync(string token, Guid wallet)
        => (await SendAsync(token, HttpMethod.Get, $"/api/v1/financial-accounts/{wallet}/balance")).Body.GetProperty("balanceCents").GetInt64();

    [Fact]
    public async Task AnotherUsersLoan_Is404_OnEveryRoute()
    {
        var owner = await LoginAsync();
        var stranger = await LoginAsync();
        var id = await LoanAsync(owner);
        var entry = (await SendAsync(owner, HttpMethod.Get, $"/api/v1/loans/{id}")).Body.GetProperty("entries")[0].GetProperty("id").GetGuid();

        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, $"/api/v1/loans/{id}", null),
                     (HttpMethod.Put, $"/api/v1/loans/{id}", new { direction = "lend", title = "X", counterparty = "Y", principalCents = 1_000_000 }),
                     (HttpMethod.Delete, $"/api/v1/loans/{id}", null),
                     (HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 100 }),
                     (HttpMethod.Post, $"/api/v1/loans/{id}/principal", new { amountCents = 100 }),
                     (HttpMethod.Delete, $"/api/v1/loans/{id}/entries/{entry}", null),
                     (HttpMethod.Post, $"/api/v1/loans/{id}/write-off", null),
                 })
        {
            (await SendAsync(stranger, method, url, body)).Status.Should().Be(HttpStatusCode.NotFound, $"{method} {url}");
        }

        (await SendAsync(owner, HttpMethod.Get, $"/api/v1/loans/{id}")).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_SentTwiceWithTheSameClientRequestId_Answers200WithTheSameLoan()
    {
        var token = await LoginAsync();
        var request = new { direction = "lend", title = "Khoản", counterparty = "Người", principalCents = 500_000, clientRequestId = Guid.NewGuid() };

        var first = await SendAsync(token, HttpMethod.Post, "/api/v1/loans", request);
        var again = await SendAsync(token, HttpMethod.Post, "/api/v1/loans", request);

        (first.Status, again.Status).Should().Be((HttpStatusCode.Created, HttpStatusCode.OK));
        again.Body.GetProperty("id").GetGuid().Should().Be(first.Body.GetProperty("id").GetGuid());
        (await SendAsync(token, HttpMethod.Get, "/api/v1/loans")).Body.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Repay_SentTwiceWithTheSameClientRequestId_MovesMoneyOnce()
    {
        var token = await LoginAsync();
        var wallet = await WalletAsync(token, 0);
        var id = await LoanAsync(token);
        var body = new { amountCents = 300_000, financialAccountId = wallet, clientRequestId = Guid.NewGuid() };

        await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{id}/repayments", body);
        var (_, again) = await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{id}/repayments", body);

        again.GetProperty("paidCents").GetInt64().Should().Be(300_000);
        (await BalanceAsync(token, wallet)).Should().Be(300_000);
    }

    [Fact]
    public async Task TwoRepaymentsAtOnce_OnlyOneFitsTheRemaining()
    {
        var token = await LoginAsync();
        var wallet = await WalletAsync(token, 0);
        var id = await LoanAsync(token, principal: 1_000_000);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { amountCents = 600_000, financialAccountId = wallet })));

        results.Select(r => r.Status).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
        var detail = (await SendAsync(token, HttpMethod.Get, $"/api/v1/loans/{id}")).Body;
        detail.GetProperty("loan").GetProperty("paidCents").GetInt64().Should().Be(600_000);
        (await BalanceAsync(token, wallet)).Should().Be(600_000, "chỉ một giao dịch thu nợ được ghi");
    }

    [Fact]
    public async Task Detail_Summary_Filter_LendMore_DeleteEntry_AndWriteOff()
    {
        var token = await LoginAsync();
        var wallet = await WalletAsync(token, 5_000_000);
        var lend = await LoanAsync(token, "lend", 1_000_000);
        var borrow = await LoanAsync(token, "borrow", 2_000_000);

        var (status, more) = await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{lend}/principal", new { amountCents = 500_000, financialAccountId = wallet });
        status.Should().Be(HttpStatusCode.OK);
        more.GetProperty("principalCents").GetInt64().Should().Be(1_500_000);
        (await BalanceAsync(token, wallet)).Should().Be(4_500_000, "cho mượn thêm là tiền ra");
        (await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{lend}/principal", new { amountCents = 0 })).Status.Should().Be(HttpStatusCode.BadRequest);

        var detail = (await SendAsync(token, HttpMethod.Get, $"/api/v1/loans/{lend}")).Body;
        detail.GetProperty("entries").EnumerateArray().Select(e => (e.GetProperty("kind").GetString(), e.GetProperty("amountCents").GetInt64()))
            .Should().Equal(("principal", 1_000_000L), ("principal", 500_000L));

        var summary = (await SendAsync(token, HttpMethod.Get, "/api/v1/loans/summary")).Body;
        (summary.GetProperty("owedToMeCents").GetInt64(), summary.GetProperty("iOweCents").GetInt64()).Should().Be((1_500_000L, 2_000_000L));

        var second = detail.GetProperty("entries")[1].GetProperty("id").GetGuid();
        var (_, afterDelete) = await SendAsync(token, HttpMethod.Delete, $"/api/v1/loans/{lend}/entries/{second}");
        afterDelete.GetProperty("principalCents").GetInt64().Should().Be(1_000_000);
        (await BalanceAsync(token, wallet)).Should().Be(4_500_000, "xoá dòng chỉ gỡ liên kết; giao dịch ở lại");

        var (_, writtenOff) = await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{borrow}/write-off");
        writtenOff.GetProperty("status").GetString().Should().Be("written_off");
        (await SendAsync(token, HttpMethod.Get, "/api/v1/loans?status=written_off")).Body.EnumerateArray().Select(l => l.GetProperty("id").GetGuid())
            .Should().Equal(borrow);
        (await SendAsync(token, HttpMethod.Get, "/api/v1/loans?direction=lend")).Body.EnumerateArray().Select(l => l.GetProperty("id").GetGuid())
            .Should().Equal(lend);
        (await SendAsync(token, HttpMethod.Get, "/api/v1/loans?status=paid")).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LinkingAConfirmedTransaction_AsARepayment_MovesItToTheLoanCategory()
    {
        var token = await LoginAsync();
        var wallet = await WalletAsync(token, 0);
        var id = await LoanAsync(token, "lend", 1_000_000);
        var income = (await SendAsync(token, HttpMethod.Get, "/api/v1/categories")).Body.EnumerateArray()
            .First(c => c.GetProperty("slug").GetString() == "income").GetProperty("id").GetGuid();
        var transaction = (await SendAsync(token, HttpMethod.Post, "/api/v1/transactions", new
        {
            financialAccountId = wallet, categoryId = income, amountCents = 400_000, transactionType = "Credit", transactedAt = DateTimeOffset.UtcNow,
        })).Body.GetProperty("id").GetGuid();

        var (status, loan) = await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { transactionId = transaction });

        status.Should().Be(HttpStatusCode.OK, loan.ToString());
        loan.GetProperty("paidCents").GetInt64().Should().Be(400_000);
        var tx = (await SendAsync(token, HttpMethod.Get, $"/api/v1/transactions/{transaction}")).Body;
        var loanCategory = (await SendAsync(token, HttpMethod.Get, "/api/v1/categories")).Body.EnumerateArray()
            .First(c => c.GetProperty("slug").GetString() == "loan").GetProperty("id").GetGuid();
        tx.GetProperty("categoryId").GetGuid().Should().Be(loanCategory);
        (await BalanceAsync(token, wallet)).Should().Be(400_000, "gắn giao dịch có sẵn không đổi số dư");
        var month = (await SendAsync(token, HttpMethod.Get, "/api/v1/reports/monthly-summary")).Body;
        (month.GetProperty("totalIncomeCents").GetInt64(), month.GetProperty("cashFlow").GetProperty("loanInflowCents").GetInt64())
            .Should().Be((0L, 400_000L), "gắn vào khoản vay thì không còn là thu nhập (Q16)");

        (await SendAsync(token, HttpMethod.Post, $"/api/v1/loans/{id}/repayments", new { transactionId = transaction })).Status
            .Should().Be(HttpStatusCode.UnprocessableEntity, "một giao dịch chỉ gắn một dòng còn sống");
    }
}
