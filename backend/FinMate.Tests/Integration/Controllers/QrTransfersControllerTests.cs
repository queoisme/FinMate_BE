using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Quét QR chuyển khoản rồi khớp với thông báo ngân hàng. Điểm đáng canh nhất là các lỗi của bản thử đã được sửa: khớp theo ngân hàng GỬI (chuyển khác ngân hàng vẫn khớp), lần quét SAU thông báo không khớp,
/// và mọi dữ liệu tách theo người dùng.
/// </summary>
[Collection("Integration")]
public class QrTransfersControllerTests : IClassFixture<AuthApiFactory>
{
    private const string MbBin = "970422";
    private const string VietinBin = "970415";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public QrTransfersControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug);
    private record TransferData(Guid Id, string Status, bool IsExpired, Guid? CategoryId, string? CategoryName, Guid? MatchedTransactionId, string SenderBankName, string ReceiverBankName);
    private record OverrideData(Guid Id, Guid CategoryId, string BankName);
    private record AnalysisData(Guid NotificationLogId, string Status, Guid? DraftTransactionId);
    private record TransactionData(Guid Id, Guid? CategoryId, string? MerchantName);
    private record BankData(string Bin, string Name, string AppCode, bool CanMatchNotifications);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> NewUserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "QR Test User"));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage Req(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.SendAsync(Req(method, url, token, body));
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private async Task<Guid> SystemCategoryAsync(string token, string slug)
    {
        var list = await SendAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token);
        return list.First(c => c.Slug == slug).Id;
    }

    private async Task<Guid> CreateBankAccountAsync(string token, string providerKey)
    {
        Guid providerId;
        using (var scope = _factory.Services.CreateScope())
        {
            providerId = (await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().ProviderConfigs.FirstAsync(p => p.ProviderKey == providerKey)).Id;
        }

        return (await SendAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token,
            new { accountName = "Tài khoản test", accountType = "Bank", providerConfigId = providerId, initialBalanceCents = 0 }, HttpStatusCode.Created)).Id;
    }

    private static object Intent(long amount, string senderBin = MbBin, string receiverBin = VietinBin, string account = "0123456789", Guid? categoryId = null, string? name = "NGUYEN VAN A")
        => new { receiverBankBin = receiverBin, receiverAccountNumber = account, receiverName = name, senderBankBin = senderBin, amountCents = amount, message = "tien an", categoryId };

    private async Task<AnalysisData> AnalyzeMbDebitAsync(string token, long amount, DateTimeOffset receivedAt, string aiCategory = "shopping")
    {
        FakeAIServiceClient.SetScenario("com.mbmobile", new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(amount, "debit", "Ten do AI doan", "Chuyen tien", receivedAt, 9_000_000, 0.91),
            new CategorizationResult(aiCategory, 0.6),
            new DuplicateResult(false, null),
            new ModelVersions("1.0", "1.0", "1.0"),
            50));

        return await SendAsync<AnalysisData>(HttpMethod.Post, "/api/v1/notifications/analyze", token, new
        {
            packageName = "com.mbmobile",
            notificationTitle = "Bien dong so du",
            notificationBody = $"TK 0123|GD: -{amount}VND {Guid.NewGuid():N}",
            receivedAt,
        });
    }

    [Fact]
    public async Task Banks_AreServedFromTheDatabase_WithTheMbBankMatchable()
    {
        var token = await NewUserTokenAsync();

        var banks = await SendAsync<List<BankData>>(HttpMethod.Get, "/api/v1/qr/banks", token);

        banks.Should().Contain(b => b.Bin == MbBin && b.AppCode == "mb" && b.CanMatchNotifications);
        banks.Should().Contain(b => b.Bin == "970429" && !b.CanMatchNotifications);  // SCB: mở app được nhưng FinMate chưa đọc thông báo của nó
    }

    [Fact]
    public async Task EveryEndpoint_NeedsALogin()
    {
        (await _client.GetAsync("/api/v1/qr/banks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/v1/qr/transfers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/v1/qr/counterparties")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("12345", "0123456789", 50_000)]      // BIN không đủ 6 chữ số
    [InlineData("970415", "12", 50_000)]             // số tài khoản quá ngắn
    [InlineData("970415", "0123456789", 0)]          // số tiền phải > 0
    public async Task Create_RejectsGarbageInput_With400(string receiverBin, string account, long amount)
    {
        var token = await NewUserTokenAsync();

        var response = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(amount, receiverBin: receiverBin, account: account)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithABankThatIsNotInTheCatalogue_Is422()
    {
        var token = await NewUserTokenAsync();

        var response = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(50_000, receiverBin: "999999")));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("QR_BANK_UNSUPPORTED");
    }

    [Fact]
    public async Task ChoosingACategory_RemembersIt_SoTheNextScanOfTheSameReceiverNeedsNoChoice_AndOnlyForThatUser()
    {
        var alice = await NewUserTokenAsync();
        var bob = await NewUserTokenAsync();
        var food = await SystemCategoryAsync(alice, "food");

        var first = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", alice, Intent(41_000, account: "5550001", categoryId: food), HttpStatusCode.Created);
        first.CategoryId.Should().Be(food);

        // Lần sau không chọn gì: server tự gắn danh mục đã nhớ.
        var second = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", alice, Intent(42_000, account: "5550001", categoryId: null), HttpStatusCode.Created);
        second.CategoryId.Should().Be(food);

        var lookup = await SendAsync<OverrideData?>(HttpMethod.Post, "/api/v1/qr/counterparties/lookup", alice, new { bankBin = VietinBin, accountNumber = "5550001" });
        lookup!.CategoryId.Should().Be(food);

        // Người dùng khác: không thấy, và quét cùng tài khoản không được gắn danh mục của Alice.
        (await SendAsync<OverrideData?>(HttpMethod.Post, "/api/v1/qr/counterparties/lookup", bob, new { bankBin = VietinBin, accountNumber = "5550001" })).Should().BeNull();
        var bobIntent = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", bob, Intent(43_000, account: "5550001"), HttpStatusCode.Created);
        bobIntent.CategoryId.Should().BeNull();
        (await SendAsync<List<OverrideData>>(HttpMethod.Get, "/api/v1/qr/counterparties", bob)).Should().BeEmpty();

        // Bob không sửa/xóa được mapping của Alice, cũng không hủy được lần quét của Alice.
        (await _client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/qr/counterparties/{lookup.Id}", bob))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/qr/transfers/{first.Id}/cancel", bob))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ACategoryThatBelongsToSomeoneElse_IsRejected()
    {
        var alice = await NewUserTokenAsync();
        var bob = await NewUserTokenAsync();
        var custom = await SendAsync<CategoryData>(HttpMethod.Post, "/api/v1/categories", alice, new { name = "Riêng của Alice", iconName = "tag" }, HttpStatusCode.Created);

        var response = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/qr/transfers", bob, Intent(44_000, categoryId: custom.Id)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATransferBetweenTwoDifferentBanks_StillMatches_TheBankThatMattersIsTheSenders()
    {
        var token = await NewUserTokenAsync();
        await CreateBankAccountAsync(token, "mb_bank");
        var food = await SystemCategoryAsync(token, "food");

        // Gửi từ MB, nhận ở VietinBank: bản thử cũ so ngân hàng NHẬN với app MB nên chuyển khác ngân hàng không bao giờ khớp.
        var intent = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token,
            Intent(123_457, senderBin: MbBin, receiverBin: VietinBin, categoryId: food), HttpStatusCode.Created);

        var analysis = await AnalyzeMbDebitAsync(token, 123_457, DateTimeOffset.UtcNow.AddMinutes(3));

        analysis.DraftTransactionId.Should().NotBeNull();
        var draft = await SendAsync<TransactionData>(HttpMethod.Get, $"/api/v1/transactions/{analysis.DraftTransactionId}", token);
        draft.CategoryId.Should().Be(food, "người dùng đã chọn tay danh mục lúc quét nên thắng gợi ý của AI");
        draft.MerchantName.Should().Be("NGUYEN VAN A");

        var matched = (await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers?status=Matched", token)).Single(t => t.Id == intent.Id);
        matched.MatchedTransactionId.Should().Be(analysis.DraftTransactionId);

        // Đã khớp thì không hủy được (sẽ làm mất dấu người nhận của giao dịch).
        (await _client.SendAsync(Req(HttpMethod.Post, $"/api/v1/qr/transfers/{intent.Id}/cancel", token))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AScanThatHappenedAfterTheNotification_DoesNotMatch()
    {
        var token = await NewUserTokenAsync();
        await CreateBankAccountAsync(token, "mb_bank");
        var intent = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(222_001), HttpStatusCode.Created);

        // Thông báo "đến" 10 phút TRƯỚC lúc quét: lần quét không thể là nguyên nhân của nó (bản thử cũ vẫn nhận).
        var analysis = await AnalyzeMbDebitAsync(token, 222_001, DateTimeOffset.UtcNow.AddMinutes(-10));

        analysis.DraftTransactionId.Should().NotBeNull();
        (await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers?status=Pending", token)).Should().Contain(t => t.Id == intent.Id);
    }

    [Fact]
    public async Task ANotificationFromAnotherSenderBank_OrAnotherAmount_DoesNotMatch()
    {
        var token = await NewUserTokenAsync();
        await CreateBankAccountAsync(token, "mb_bank");
        var viaVietin = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(333_001, senderBin: VietinBin), HttpStatusCode.Created);
        var otherAmount = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(333_002), HttpStatusCode.Created);

        await AnalyzeMbDebitAsync(token, 333_001, DateTimeOffset.UtcNow.AddMinutes(1));  // thông báo từ MB, nhưng người dùng chọn gửi từ VietinBank
        await AnalyzeMbDebitAsync(token, 333_999, DateTimeOffset.UtcNow.AddMinutes(1));  // đúng ngân hàng, sai số tiền

        var pending = await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers?status=Pending", token);
        pending.Select(t => t.Id).Should().Contain([viaVietin.Id, otherAmount.Id]);
    }

    [Fact]
    public async Task EditingTheCategoryOfAMatchedTransaction_UpdatesTheRememberedCategoryForThatReceiver()
    {
        var token = await NewUserTokenAsync();
        var accountId = await CreateBankAccountAsync(token, "mb_bank");
        var food = await SystemCategoryAsync(token, "food");
        var transport = await SystemCategoryAsync(token, "transport");
        await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(444_001, account: "7770001", categoryId: food), HttpStatusCode.Created);
        var analysis = await AnalyzeMbDebitAsync(token, 444_001, DateTimeOffset.UtcNow.AddMinutes(1));

        await SendAsync<TransactionData>(HttpMethod.Patch, $"/api/v1/transactions/{analysis.DraftTransactionId}", token, new
        {
            financialAccountId = accountId, counterAccountId = (Guid?)null, categoryId = transport, amountCents = 444_001,
            transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, merchantName = "NGUYEN VAN A", description = "tien an",
        });

        var remembered = await SendAsync<OverrideData?>(HttpMethod.Post, "/api/v1/qr/counterparties/lookup", token, new { bankBin = VietinBin, accountNumber = "7770001" });
        remembered!.CategoryId.Should().Be(transport);
    }

    [Fact]
    public async Task CancellingAPendingTransfer_KeepsItInTheHistory_AndCancellingTwiceIsFine()
    {
        var token = await NewUserTokenAsync();
        var intent = await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(555_001), HttpStatusCode.Created);

        var cancelled = await SendAsync<TransferData>(HttpMethod.Post, $"/api/v1/qr/transfers/{intent.Id}/cancel", token);
        var again = await SendAsync<TransferData>(HttpMethod.Post, $"/api/v1/qr/transfers/{intent.Id}/cancel", token);

        cancelled.Status.Should().Be("Cancelled");
        again.Status.Should().Be("Cancelled");
        (await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers", token)).Should().Contain(t => t.Id == intent.Id);
    }

    [Fact]
    public async Task AnAmountAboveTheConfiguredCeiling_IsRejected_With400()
    {
        var token = await NewUserTokenAsync();

        var response = await _client.SendAsync(Req(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(100_000_000_001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EverySeededBankProviderKey_ExistsInProviderConfigs()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();

        var dangling = await FinMate.Infrastructure.Persistence.Seed.VietQrBankSeeder.FindDanglingProviderKeysAsync(db);

        dangling.Should().BeEmpty("khớp thông báo đi qua provider_key bằng chuỗi, một khóa lạc làm ngân hàng đó âm thầm không bao giờ khớp");
    }

    [Fact]
    public async Task TheMatchWindow_ComesFromTheSystemConfig_NotFromTheCode()
    {
        var admin = await AdminTokenAsync();
        var token = await NewUserTokenAsync();
        await CreateBankAccountAsync(token, "mb_bank");
        await SendAsync<TransferData>(HttpMethod.Post, "/api/v1/qr/transfers", token, Intent(666_001), HttpStatusCode.Created);

        // Thông báo đến 8 phút sau lúc quét: khớp với cửa sổ mặc định 30 phút, nhưng KHÔNG khớp khi admin thu hẹp còn 5 phút.
        var original = await SetMatchWindowAsync(admin, 5);
        try
        {
            var narrow = await AnalyzeMbDebitAsync(token, 666_001, DateTimeOffset.UtcNow.AddMinutes(8));
            narrow.DraftTransactionId.Should().NotBeNull();
            (await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers?status=Pending", token)).Should().NotBeEmpty();
        }
        finally
        {
            await SetMatchWindowAsync(admin, original);
        }

        var wide = await AnalyzeMbDebitAsync(token, 666_001, DateTimeOffset.UtcNow.AddMinutes(8).AddSeconds(5));
        wide.DraftTransactionId.Should().NotBeNull();
        (await SendAsync<List<TransferData>>(HttpMethod.Get, "/api/v1/qr/transfers?status=Matched", token)).Should().NotBeEmpty();
    }

    private async Task<string> AdminTokenAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        return (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
    }

    /// <summary>Đặt cửa sổ khớp (phút) qua API admin như người dùng thật, trả về giá trị cũ để khôi phục.</summary>
    private async Task<int> SetMatchWindowAsync(string admin, int minutes)
    {
        var current = await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/admin/system-config", admin));
        var view = JsonNode.Parse(await current.Content.ReadAsStringAsync())!["data"]!;
        var content = view["content"]!.DeepClone();
        var old = content["qrTransfer"]!["matchWindowMinutes"]!.GetValue<int>();
        content["qrTransfer"]!["matchWindowMinutes"] = minutes;

        var put = await _client.SendAsync(Req(HttpMethod.Put, "/api/v1/admin/system-config", admin,
            new { expectedVersion = view["version"]!.GetValue<int>(), content = JsonSerializer.Deserialize<JsonElement>(content.ToJsonString()), note = "thử cửa sổ khớp" }));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        return old;
    }
}
