using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Hai kênh nhập bổ sung của docx phương thức 2 và 3.
/// </summary>
[Collection("Integration")]
public class ReceiptAndVoiceControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ReceiptAndVoiceControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record TransactionData(Guid Id, long AmountCents, string Source, string Status);
    private record TransactionBody(bool Success, TransactionData Data);
    private record ScannedData(
        string OcrResult, long? AmountCents, string? TransactionType,
        string? MerchantName, string? CategorySlug, double? Confidence);
    private record ScannedBody(bool Success, ScannedData Data);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Receipt Test User"));

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var body = await response.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return body!.Data.AccessToken;
    }

    private async Task<Guid> CreateCashAccountAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/financial-accounts")
        {
            Content = JsonContent.Create(
                new CreateFinancialAccountRequest("Ví tiền mặt", AccountType.Cash, null, 5_000_000),
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions);
        return body!.Data.Id;
    }

    private static MultipartFormDataContent ImageContent(
        string contentType = "image/jpeg", int size = 64)
    {
        var file = new ByteArrayContent(new byte[size]);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "hoa-don.jpg" } };
    }

    private HttpRequestMessage ScanRequest(string? token, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions/scan-receipt")
        {
            Content = content,
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    // ----------------------------------------------------------- scan receipt

    [Fact]
    public async Task ScanningAReceiptRequiresAuthentication()
    {
        var response = await _client.SendAsync(ScanRequest(null, ImageContent()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AReceiptComesBackAsPrefillFieldsAndCreatesNothing()
    {
        // Docx phương thức 3 yêu cầu người dùng rà soát trước khi lưu, nên endpoint này
        // KHÔNG được tạo giao dịch — nó chỉ trả về thứ để điền sẵn vào form.
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(ScanRequest(token, ImageContent()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var scanned = (await response.Content.ReadFromJsonAsync<ScannedBody>(JsonOptions))!.Data;
        scanned.OcrResult.Should().Be("success");
        scanned.AmountCents.Should().Be(110_000);
        scanned.TransactionType.Should().Be("debit");
        scanned.CategorySlug.Should().Be("shopping");

        var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/transactions");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var listResponse = await _client.SendAsync(list);
        var raw = await listResponse.Content.ReadAsStringAsync();
        raw.Should().NotContain("110000", "quét hóa đơn không được tự tạo giao dịch");
    }

    [Fact]
    public async Task ScannedConfidenceStaysBelowTheOneTapThreshold()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(ScanRequest(token, ImageContent()));
        var scanned = (await response.Content.ReadFromJsonAsync<ScannedBody>(JsonOptions))!.Data;

        scanned.Confidence.Should().BeLessThan(0.85);
    }

    [Fact]
    public async Task AnEmptyUploadIsRejected()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(ScanRequest(token, ImageContent(size: 0)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code
            .Should().Be("TRANSACTION_RECEIPT_IMAGE_INVALID");
    }

    [Fact]
    public async Task ANonImageUploadIsRejected()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            ScanRequest(token, ImageContent(contentType: "application/pdf")));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code
            .Should().Be("TRANSACTION_RECEIPT_IMAGE_INVALID");
    }

    [Fact]
    public async Task AnOversizedUploadIsRefusedByTheRequestSizeLimit()
    {
        // Giới hạn đặt ở tầng ASP.NET Core nên request bị cắt TRƯỚC khi đọc hết vào bộ nhớ —
        // guard trong handler là lớp thứ hai, cho đường gọi không đi qua HTTP.
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(
            ScanRequest(token, ImageContent(size: ScanReceiptCommandHandler.MaxImageBytes + 4096)));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.RequestEntityTooLarge,
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity);
    }

    // ----------------------------------------------------------- kênh nhập

    [Theory]
    [InlineData(TransactionSource.Voice)]
    [InlineData(TransactionSource.Receipt)]
    [InlineData(TransactionSource.Manual)]
    public async Task AUserCanDeclareWhichChannelTheyUsed(TransactionSource source)
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(
                new CreateManualTransactionRequest(
                    accountId, null, 45_000, TransactionType.Debit,
                    DateTimeOffset.UtcNow, "Highlands Coffee", null, Source: source),
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data;
        created.Source.Should().Be(source.ToString());
        created.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task AClientCannotPassOffItsOwnEntryAsAiDetected()
    {
        // Source=Notification là điều kiện lọc của tỉ lệ "người dùng sửa lại danh mục AI
        // đoán" ở /admin/ai-stats. Khai bừa được thì thước đo đó bị bóp méo âm thầm.
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(
                new CreateManualTransactionRequest(
                    accountId, null, 45_000, TransactionType.Debit,
                    DateTimeOffset.UtcNow, "Highlands Coffee", null,
                    Source: TransactionSource.Notification),
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OmittingTheChannelStillWorksForOlderClients()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CreateCashAccountAsync(token);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = JsonContent.Create(
                new CreateManualTransactionRequest(
                    accountId, null, 45_000, TransactionType.Debit,
                    DateTimeOffset.UtcNow, "Highlands Coffee", null),
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<TransactionBody>(JsonOptions))!.Data.Source
            .Should().Be(nameof(TransactionSource.Manual));
    }
}
