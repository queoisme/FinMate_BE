using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 92 qua API thật: trường kỳ cuối khi tạo/sửa/xác nhận, đánh dấu tay và gỡ kỳ (BR-INS-04), tất toán (BR-INS-08), khoản mới sau khi hoàn thành
/// (BR-INS-09), lọc <c>Completed</c> (BR-INS-10, D16); 404 với id của người khác, 400/409/422, gọi lại an toàn.
/// </summary>
[Collection("Integration")]
public class RecurringInstallmentsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;

    public RecurringInstallmentsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    internal record ItemData(
        Guid Id, string Status, DateOnly NextDueDate, long AmountCents, int? TotalOccurrences, int PrepaidOccurrences, int PaidOccurrences,
        int? RemainingOccurrences, long? RemainingAmountCents, DateOnly? EndDate, int? AnchorDay, DateOnly? FinalDueDate, DateTimeOffset? CompletedAt,
        bool SettledEarly);

    private record PaymentData(Guid Id, Guid? TransactionId, DateOnly DueDate, string Kind);
    private record PaymentResult(ItemData Item, PaymentData Payment);

    private static DateOnly Today => VietnamTime.Today();

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Installment Test"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await SendRawAsync(method, url, token, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        if (code is not null)
        {
            (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
        }
    }

    private async Task<Guid> WalletAsync(string token)
        => (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "VCB", accountType = "Cash", initialBalanceCents = 50_000_000 }, HttpStatusCode.Created)).Id;

    private Task<ItemData> InstallmentAsync(string token, int total = 12, int prepaid = 9, DateOnly? due = null, string counterparty = "FE Credit")
        => SendAsync<ItemData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Trả góp điện thoại", counterpartyName = counterparty, amountCents = 1_200_000, cadence = "Monthly",
            nextDueDate = due ?? Today.AddDays(10), totalOccurrences = total, prepaidOccurrences = prepaid,
        }, HttpStatusCode.Created);

    private async Task<Guid> TransactionAsync(string token, Guid wallet, string type = "Debit", string merchant = "FE Credit", long amount = 1_200_000)
        => (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = wallet, amountCents = amount, transactionType = type, transactedAt = DateTimeOffset.UtcNow, merchantName = merchant,
        }, HttpStatusCode.Created)).Id;

    [Fact]
    public async Task Create_ReturnsTheProgress_AndDefaultsTheAnchorToTheDueDay()
    {
        var token = await NewUserTokenAsync();
        var due = Today.AddDays(10);

        var item = await InstallmentAsync(token, due: due);

        item.Should().BeEquivalentTo(new
        {
            Status = "active", TotalOccurrences = (int?)12, PrepaidOccurrences = 9, PaidOccurrences = 9, RemainingOccurrences = (int?)3,
            RemainingAmountCents = (long?)3_600_000, AnchorDay = (int?)due.Day, SettledEarly = false,
        });
        item.FinalDueDate.Should().Be(due.AddMonths(2), "kỳ 12 là hai tháng sau kỳ 10 (AddMonths cũng về ngày cuối tháng như anchor)");
    }

    [Fact]
    public async Task Create_WithoutEndFields_BehavesAsBefore()
    {
        var token = await NewUserTokenAsync();

        var item = await SendAsync<ItemData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Netflix", counterpartyName = "Netflix", amountCents = 260_000, cadence = "Monthly", nextDueDate = Today.AddDays(3),
        }, HttpStatusCode.Created);

        item.TotalOccurrences.Should().BeNull();
        item.RemainingOccurrences.Should().BeNull();
        item.FinalDueDate.Should().BeNull();
        item.PrepaidOccurrences.Should().Be(0);
    }

    [Fact]
    public async Task Create_RejectsInvalidEndFields()
    {
        var token = await NewUserTokenAsync();
        object Body(object extra) => new Dictionary<string, object?>
        {
            ["direction"] = "Outflow", ["name"] = "X", ["counterpartyName"] = "X", ["amountCents"] = 100, ["cadence"] = "Monthly",
            ["nextDueDate"] = Today.AddDays(3),
        }.Concat(extra.GetType().GetProperties().Select(p => new KeyValuePair<string, object?>(p.Name, p.GetValue(extra)))).ToDictionary();

        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, Body(new { totalOccurrences = 12, endDate = Today.AddYears(1) })), HttpStatusCode.BadRequest);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, Body(new { totalOccurrences = 601 })), HttpStatusCode.BadRequest);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, Body(new { totalOccurrences = 12, prepaidOccurrences = 12 })), HttpStatusCode.BadRequest);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, Body(new { endDate = Today })), HttpStatusCode.BadRequest);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, Body(new { anchorDay = 32 })), HttpStatusCode.BadRequest);
        var weekly = Body(new { anchorDay = 15 });
        ((Dictionary<string, object?>)weekly)["cadence"] = "Weekly";
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, weekly), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ScenarioF_CashPaymentMarkedByHand_CountsAndMovesTheDueDate()
    {
        var token = await NewUserTokenAsync();
        var item = await InstallmentAsync(token, prepaid: 10);

        var result = await SendAsync<PaymentResult>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { }, HttpStatusCode.Created);

        result.Item.PaidOccurrences.Should().Be(11);
        result.Item.NextDueDate.Should().Be(item.NextDueDate.AddMonths(1));
        result.Payment.Should().BeEquivalentTo(new { TransactionId = (Guid?)null, DueDate = item.NextDueDate, Kind = "manual" });
        (await SendAsync<List<PaymentData>>(HttpMethod.Get, $"/api/v1/recurring-items/{item.Id}/payments", token)).Should().ContainSingle();
    }

    [Fact]
    public async Task MarkingWithATransaction_ChecksOwnershipDirectionAndDoubleUse()
    {
        var token = await NewUserTokenAsync();
        var wallet = await WalletAsync(token);
        var item = await InstallmentAsync(token, prepaid: 0);
        var other = await InstallmentAsync(token, prepaid: 0, counterparty: "Home Credit");
        var debit = await TransactionAsync(token, wallet, merchant: "Chuyển khoản");
        var credit = await TransactionAsync(token, wallet, type: "Credit", merchant: "Lương");
        var stranger = await NewUserTokenAsync();
        var strangersDebit = await TransactionAsync(stranger, await WalletAsync(stranger));

        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { transactionId = credit }),
            HttpStatusCode.UnprocessableEntity, "RECURRING_ITEM_PAYMENT_INVALID");
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { transactionId = strangersDebit }),
            HttpStatusCode.NotFound);

        var linked = await SendAsync<PaymentResult>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { transactionId = debit }, HttpStatusCode.Created);
        linked.Payment.TransactionId.Should().Be(debit);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{other.Id}/payments", token, new { transactionId = debit }),
            HttpStatusCode.Conflict, "RECURRING_ITEM_PAYMENT_LINKED");
    }

    [Fact]
    public async Task RemovingAPayment_RewindsAndReopens_AndIsSafeToRepeat()
    {
        var token = await NewUserTokenAsync();
        var item = await InstallmentAsync(token, total: 3, prepaid: 2);
        var paid = await SendAsync<PaymentResult>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", token, new { }, HttpStatusCode.Created);
        paid.Item.Status.Should().Be("completed");
        paid.Item.CompletedAt.Should().NotBeNull();

        (await SendRawAsync(HttpMethod.Delete, $"/api/v1/recurring-items/{item.Id}/payments/{paid.Payment.Id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendRawAsync(HttpMethod.Delete, $"/api/v1/recurring-items/{item.Id}/payments/{paid.Payment.Id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reopened = (await SendAsync<List<ItemData>>(HttpMethod.Get, "/api/v1/recurring-items", token)).Single(i => i.Id == item.Id);
        reopened.Status.Should().Be("active");
        reopened.PaidOccurrences.Should().Be(2);
        reopened.NextDueDate.Should().Be(item.NextDueDate);
        reopened.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task ScenarioH_SettleEarly_CompletesAndIsIdempotent_AndCanBeReopened()
    {
        var token = await NewUserTokenAsync();
        var item = await InstallmentAsync(token, total: 12, prepaid: 6);

        var settled = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/settle", token, new { });
        var again = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/settle", token, new { });

        settled.Status.Should().Be("completed");
        settled.SettledEarly.Should().BeTrue();
        again.Should().BeEquivalentTo(settled);

        var reopened = await SendAsync<ItemData>(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { status = "Active" });
        reopened.Status.Should().Be("active");
        reopened.SettledEarly.Should().BeFalse();
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { status = "Active" }),
            HttpStatusCode.Conflict, "RECURRING_ITEM_STATE");
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { status = "Paused" }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settle_WithoutAnEnd_Is422()
    {
        var token = await NewUserTokenAsync();
        var netflix = await SendAsync<ItemData>(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Netflix", counterpartyName = "Netflix", amountCents = 260_000, cadence = "Monthly", nextDueDate = Today.AddDays(3),
        }, HttpStatusCode.Created);

        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{netflix.Id}/settle", token, new { }),
            HttpStatusCode.UnprocessableEntity, "RECURRING_ITEM_NO_FINAL");
    }

    [Fact]
    public async Task ScenarioI_AfterCompletion_ANewInstallmentWithTheSameCompany_IsCreated()
    {
        var token = await NewUserTokenAsync();
        var first = await InstallmentAsync(token, total: 2, prepaid: 1);
        await SendAsync<PaymentResult>(HttpMethod.Post, $"/api/v1/recurring-items/{first.Id}/payments", token, new { }, HttpStatusCode.Created);

        var second = await InstallmentAsync(token, total: 6, prepaid: 0);

        second.Id.Should().NotBe(first.Id);
        (await SendAsync<List<ItemData>>(HttpMethod.Get, "/api/v1/recurring-items", token)).Select(i => i.Id).Should().Equal(second.Id);
        (await SendAsync<List<ItemData>>(HttpMethod.Get, "/api/v1/recurring-items?status=Completed", token)).Select(i => i.Id).Should().Equal(first.Id);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, "/api/v1/recurring-items", token, new
        {
            direction = "Outflow", name = "Lần ba", counterpartyName = "FE Credit", amountCents = 1, cadence = "Monthly", nextDueDate = Today,
        }), HttpStatusCode.Conflict, "RECURRING_ITEM_DUPLICATE");
    }

    [Fact]
    public async Task Patch_PrepaidOrTotal_CompletesOrReopens_AndClearEndRemovesTheEnd()
    {
        var token = await NewUserTokenAsync();
        var item = await InstallmentAsync(token, total: 12, prepaid: 9);

        (await SendAsync<ItemData>(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { prepaidOccurrences = 12 })).Status.Should().Be("completed");
        (await SendAsync<ItemData>(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { totalOccurrences = 24 })).Status.Should().Be("active");
        var open = await SendAsync<ItemData>(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { clearEnd = true });
        open.TotalOccurrences.Should().BeNull();
        open.RemainingOccurrences.Should().BeNull();
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { clearEnd = true, totalOccurrences = 3 }),
            HttpStatusCode.BadRequest);
        (await SendAsync<ItemData>(HttpMethod.Patch, $"/api/v1/recurring-items/{item.Id}", token, new { anchorDay = 31 })).AnchorDay.Should().Be(31);
    }

    [Fact]
    public async Task AnotherUsersItemOrPayment_IsNotFound()
    {
        var owner = await NewUserTokenAsync();
        var item = await InstallmentAsync(owner);
        var paid = await SendAsync<PaymentResult>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", owner, new { }, HttpStatusCode.Created);
        var stranger = await NewUserTokenAsync();

        (await SendRawAsync(HttpMethod.Get, $"/api/v1/recurring-items/{item.Id}/payments", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/payments", stranger, new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Delete, $"/api/v1/recurring-items/{item.Id}/payments/{paid.Payment.Id}", stranger)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/settle", stranger, new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendRawAsync(HttpMethod.Delete, $"/api/v1/recurring-items/{item.Id}/payments/{Guid.NewGuid()}", owner)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConfirmingASuggestion_AcceptsEndFields_AndStillWorksWithoutABody()
    {
        var token = await NewUserTokenAsync();
        var item = await InstallmentAsync(token);

        (await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/confirm", token)).StatusCode.Should().Be(HttpStatusCode.OK, "khoản đã active: xác nhận lại như cũ");
        var confirmed = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/{item.Id}/confirm", token, new { totalOccurrences = 10 });
        confirmed.TotalOccurrences.Should().Be(10);
        confirmed.RemainingOccurrences.Should().Be(1);
    }

    [Fact]
    public async Task FromTransaction_WithoutABody_IsUnchanged_AndAcceptsEndFields()
    {
        var token = await NewUserTokenAsync();
        var wallet = await WalletAsync(token);
        var netflix = await TransactionAsync(token, wallet, merchant: "Netflix", amount: 260_000);
        var phone = await TransactionAsync(token, wallet, merchant: "Home Credit");

        var plain = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/from-transaction/{netflix}", token, expected: HttpStatusCode.Created);
        var installment = await SendAsync<ItemData>(HttpMethod.Post, $"/api/v1/recurring-items/from-transaction/{phone}", token,
            new { totalOccurrences = 6, prepaidOccurrences = 1 }, HttpStatusCode.Created);

        plain.TotalOccurrences.Should().BeNull();
        plain.AnchorDay.Should().Be(Today.Day, "ngày gốc là ngày của giao dịch");
        plain.NextDueDate.Should().BeAfter(Today);
        installment.RemainingOccurrences.Should().Be(5);
        await ShouldFailAsync(await SendRawAsync(HttpMethod.Post, $"/api/v1/recurring-items/from-transaction/{await TransactionAsync(token, wallet, merchant: "Gym")}", token,
            new { cadence = "Weekly", anchorDay = 3 }), HttpStatusCode.BadRequest);
    }
}
