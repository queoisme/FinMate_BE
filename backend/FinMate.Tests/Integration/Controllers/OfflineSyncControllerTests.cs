using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Docx mục "Mạng Offline": app xếp thông báo/giao dịch vào Room khi mất mạng rồi đồng bộ lên
/// khi có mạng trở lại. Một hàng đợi như thế CHẮC CHẮN sẽ gửi lại — timeout giữa chừng, app bị
/// kill, hoặc backoff — nên phía server phải chịu được việc nhận lại đúng thứ đã nhận.
/// </summary>
[Collection("Integration")]
public class OfflineSyncControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public OfflineSyncControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record TransactionData(Guid Id, long AmountCents, string Status);
    private record TransactionBody(bool Success, TransactionData Data);
    private record TransactionListBody(bool Success, List<TransactionData> Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Offline Sync User"));

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

    private async Task<Guid> CreateCashAccountAsync(string token)
    {
        var response = await _client.SendAsync(Request(
            HttpMethod.Post, "/api/v1/financial-accounts", token,
            new CreateFinancialAccountRequest("Ví tiền mặt", AccountType.Cash, null, 50_000_000)));

        return (await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions))!.Data.Id;
    }

    private async Task<int> CountTransactionsAsync(string token)
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/transactions", token));
        return (await response.Content.ReadFromJsonAsync<TransactionListBody>(JsonOptions))!.Data.Count;
    }

    [Fact]
    public async Task ResendingAQueuedTransactionDoesNotCreateASecondOne()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);
        var clientRequestId = Guid.NewGuid();

        var payload = new CreateManualTransactionRequest(
            accountId, null, 45_000, TransactionType.Debit,
            DateTimeOffset.UtcNow, "Highlands Coffee", null, ClientRequestId: clientRequestId);

        var first = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, payload));
        var second = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, payload));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "lần hai không tạo ra gì nên không phải 201");

        var firstId = (await first.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Id;
        var secondId = (await second.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Id;
        secondId.Should().Be(firstId, "phải trả lại chính giao dịch cũ");

        (await CountTransactionsAsync(token)).Should().Be(1);
    }

    [Fact]
    public async Task ResendingDoesNotMoveTheBalanceTwice()
    {
        // Chỗ đắt nhất nếu sai: trừ tiền hai lần thì số dư lệch mà không có gì báo.
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        var payload = new CreateManualTransactionRequest(
            accountId, null, 45_000, TransactionType.Debit,
            DateTimeOffset.UtcNow, "Highlands Coffee", null, ClientRequestId: Guid.NewGuid());

        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, payload));
        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, payload));

        var response = await _client.SendAsync(Request(
            HttpMethod.Get, $"/api/v1/financial-accounts/{accountId}/balance", token));
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("49955000", "50.000.000 − 45.000, trừ đúng MỘT lần");
    }

    [Fact]
    public async Task TwoDifferentPurchasesAreBothKept()
    {
        // Hai ly cà phê cùng giá cùng quán trong cùng một phút là hai giao dịch THẬT. Chỉ
        // client mới phân biệt được đâu là gửi lại, nên id khác nhau phải cho ra hai bản ghi.
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        CreateManualTransactionRequest Payload() => new(
            accountId, null, 45_000, TransactionType.Debit,
            DateTimeOffset.UtcNow, "Highlands Coffee", null, ClientRequestId: Guid.NewGuid());

        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, Payload()));
        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, Payload()));

        (await CountTransactionsAsync(token)).Should().Be(2);
    }

    [Fact]
    public async Task OmittingTheIdStillWorksForOlderClients()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        var payload = new CreateManualTransactionRequest(
            accountId, null, 45_000, TransactionType.Debit,
            DateTimeOffset.UtcNow, "Highlands Coffee", null);

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", token, payload));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OneUsersIdCannotCollideWithAnothers()
    {
        // Id do client sinh nên không thể tin nó duy nhất toàn hệ thống; ràng buộc là
        // (user_id, client_request_id).
        var shared = Guid.NewGuid();

        var firstToken = await RegisterAndLoginAsync();
        var firstAccount = await CreateCashAccountAsync(firstToken);
        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", firstToken,
            new CreateManualTransactionRequest(firstAccount, null, 45_000, TransactionType.Debit,
                DateTimeOffset.UtcNow, "Quán A", null, ClientRequestId: shared)));

        var secondToken = await RegisterAndLoginAsync();
        var secondAccount = await CreateCashAccountAsync(secondToken);
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions", secondToken,
            new CreateManualTransactionRequest(secondAccount, null, 99_000, TransactionType.Debit,
                DateTimeOffset.UtcNow, "Quán B", null, ClientRequestId: shared)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountTransactionsAsync(secondToken)).Should().Be(1);
    }

    [Fact]
    public async Task ATransferResentIsAlsoDeduplicated()
    {
        var token = await RegisterAndLoginAsync();
        var from = await CreateCashAccountAsync(token);
        var to = await CreateCashAccountAsync(token);

        var payload = new CreateTransferRequest(
            from, to, 100_000, DateTimeOffset.UtcNow, "Nạp ví", ClientRequestId: Guid.NewGuid());

        var first = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions/transfer", token, payload));
        var second = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/transactions/transfer", token, payload));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountTransactionsAsync(token)).Should().Be(1);
    }
}
