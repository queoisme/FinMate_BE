using System.Net;
using System.Net.Http.Headers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 91: <c>GET /exports/transactions.csv</c> qua HTTP thật (BR-EXP-01…05, S1, S3).</summary>
[Collection("Integration")]
public class DataExportCsvTests : IClassFixture<AuthApiFactory>
{
    private const string Url = "/api/v1/exports/transactions.csv";
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportCsvTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    private static async Task<byte[]> CsvAsync(LoanTestApi api, string query = "")
    {
        var response = await api.SendAsync(HttpMethod.Get, Url + query);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
        return bytes;
    }

    private static Task InsertDraftAsync(LoanTestApi api, Guid wallet, long amount, DateTimeOffset? deletedAt = null) => api.InDbAsync(async db =>
    {
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), UserId = api.UserId, FinancialAccountId = wallet, AmountCents = amount, TransactionType = TransactionType.Debit,
            Source = TransactionSource.Notification, Status = TransactionStatus.Draft, TransactedAt = DateTimeOffset.UtcNow, MerchantName = "Nháp",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, DeletedAt = deletedAt,
        });
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task ReturnsAUtf8BomCsvAttachment_WithTheVietnameseHeader_AndOnlyConfirmedByDefault()
    {
        var api = await UserAsync();
        var wallet = await api.WalletAsync("Ví tiền mặt");
        var food = await api.CategoryAsync("food");
        var id = await api.TransactionAsync(wallet, food, 45_000, merchant: "Phở Thìn");
        await InsertDraftAsync(api, wallet, 99_000);

        var response = await api.SendAsync(HttpMethod.Get, Url);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be("text/csv; charset=utf-8");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().MatchRegex("^\"?finmate-giao-dich-\\d{4}-\\d{2}-01-\\d{4}-\\d{2}-\\d{2}\\.csv\"?$");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

        var rows = CsvTestReader.Read(bytes);
        rows.Should().ContainSingle();
        rows[0]["Mã giao dịch"].Should().Be(id.ToString());
        rows[0]["Ví"].Should().Be("Ví tiền mặt");
        rows[0]["Nơi chi"].Should().Be("Phở Thìn");
        rows[0]["Số tiền"].Should().Be("45000");
        rows[0]["Thời điểm"].Should().EndWith("+07:00");
    }

    [Fact]
    public async Task StatusDraftAndAll_IncludeDrafts_ButNeverDeletedRows()
    {
        var api = await UserAsync();
        var wallet = await api.WalletAsync();
        await api.TransactionAsync(wallet, null, 10_000);
        await InsertDraftAsync(api, wallet, 20_000);
        await InsertDraftAsync(api, wallet, 30_000, deletedAt: DateTimeOffset.UtcNow);

        CsvTestReader.Read(await CsvAsync(api, "?status=Draft")).Select(r => r["Số tiền"]).Should().Equal("20000");
        CsvTestReader.Read(await CsvAsync(api, "?status=All")).Select(r => r["Trạng thái"]).Should().BeEquivalentTo("Đã xác nhận", "Chờ duyệt");
    }

    [Fact]
    public async Task Semicolon_SplitsColumnsWithSemicolons_AndKeepsCommasInsideCells()
    {
        var api = await UserAsync();
        var wallet = await api.WalletAsync("Ví, chính");
        await api.TransactionAsync(wallet, null, 10_000, merchant: "Bún; chả");

        var bytes = await CsvAsync(api, "?delimiter=semicolon");

        System.Text.Encoding.UTF8.GetString(bytes).Should().Contain(";Ví, chính;").And.Contain("\"Bún; chả\"");
        var row = CsvTestReader.Read(bytes, ';').Single();
        row["Ví"].Should().Be("Ví, chính");
        row["Nơi chi"].Should().Be("Bún; chả");
    }

    [Fact]
    public async Task WalletAndCategoryFilters_Work_AndAnotherUsersWalletIs404()
    {
        var api = await UserAsync();
        var (a, b) = (await api.WalletAsync("A"), await api.WalletAsync("B"));
        var food = await api.CategoryAsync("food");
        await api.TransactionAsync(a, food, 1_000);
        await api.TransactionAsync(b, null, 2_000);
        var other = await UserAsync();
        var theirs = await other.WalletAsync("Của người khác");
        await other.TransactionAsync(theirs, null, 3_000);

        CsvTestReader.Read(await CsvAsync(api, $"?accountId={b}")).Select(r => r["Số tiền"]).Should().Equal("2000");
        CsvTestReader.Read(await CsvAsync(api, $"?categoryId={food}")).Select(r => r["Số tiền"]).Should().Equal("1000");
        CsvTestReader.Read(await CsvAsync(api)).Select(r => r["Số tiền"]).Should().BeEquivalentTo("1000", "2000");
        (await api.SendAsync(HttpMethod.Get, $"{Url}?accountId={theirs}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BadParameters_AreJsonErrors()
    {
        var api = await UserAsync();

        (await api.ErrorCodeAsync(HttpMethod.Get, $"{Url}?delimiter=tab", null, HttpStatusCode.BadRequest)).Should().Be("VALIDATION_ERROR");
        (await api.ErrorCodeAsync(HttpMethod.Get, $"{Url}?from=2025-01-01&to=2026-11-25", null, HttpStatusCode.UnprocessableEntity))
            .Should().Be("EXPORT_RANGE_TOO_LARGE");
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        (await _client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The21stFileInAnHour_Is429_AndRefusedRequestsDoNotCount()
    {
        var api = await UserAsync();
        (await api.SendAsync(HttpMethod.Get, $"{Url}?delimiter=tab")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        for (var i = 0; i < 20; i++)
        {
            await CsvAsync(api);
        }

        (await api.ErrorCodeAsync(HttpMethod.Get, Url, null, HttpStatusCode.TooManyRequests)).Should().Be("EXPORT_RATE_LIMITED");
        (await CsvAsync(await UserAsync())).Should().NotBeEmpty("hạn mức tính theo từng user");
    }
}
