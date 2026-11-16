using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Mọi danh sách admin/ops phải do SERVER phân trang và tìm kiếm: cùng bộ lọc thì các trang rời nhau và cộng lại đủ tổng,
/// <c>limit</c> quá lớn bị kẹp, và tìm kiếm không phân biệt hoa thường lẫn dấu tiếng Việt.
/// </summary>
[Collection("Integration")]
public class AdminListPagingTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    private string? _token;

    public AdminListPagingTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record Meta(int? Total, int? Offset, int? Limit);
    private record ListBody(List<JsonElement> Data, Meta? Meta);
    private record TokenData(string AccessToken);
    private record TokenBody(TokenData Data);

    private async Task<string> TokenAsync()
    {
        if (_token is not null)
        {
            return _token;
        }

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        return _token = (await response.Content.ReadFromJsonAsync<TokenBody>(JsonOptions))!.Data.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private async Task<ListBody> ListAsync(string url)
    {
        var response = await SendAsync(HttpMethod.Get, url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return (await response.Content.ReadFromJsonAsync<ListBody>(JsonOptions))!;
    }

    private static string IdOf(JsonElement row) => row.GetProperty("id").GetString()!;

    [Theory]
    [InlineData("/api/v1/admin/categories")]
    [InlineData("/api/v1/admin/provider-configs")]
    [InlineData("/api/v1/admin/finance-methods")]
    [InlineData("/api/v1/admin/goal-templates")]
    [InlineData("/api/v1/admin/survey-questions")]
    public async Task CatalogsPageWithStableOrderAndAReportedTotal(string path)
    {
        var all = await ListAsync($"{path}?limit=100");
        var total = all.Meta!.Total!.Value;
        total.Should().BeGreaterThan(2, "dữ liệu seed phải đủ để chia trang");
        all.Data.Should().HaveCount(Math.Min(total, 100));

        var first = await ListAsync($"{path}?limit=2&offset=0");
        var second = await ListAsync($"{path}?limit=2&offset=2");

        first.Data.Should().HaveCount(2);
        first.Meta!.Total.Should().Be(total);
        first.Meta.Limit.Should().Be(2);
        second.Meta!.Offset.Should().Be(2);
        first.Data.Select(IdOf).Intersect(second.Data.Select(IdOf)).Should().BeEmpty("các trang không được chồng nhau");
        all.Data.Take(4).Select(IdOf).Should().Equal(first.Data.Select(IdOf).Concat(second.Data.Select(IdOf)), "thứ tự phải ổn định giữa các lần gọi");
    }

    [Theory]
    [InlineData("/api/v1/admin/categories")]
    [InlineData("/api/v1/admin/provider-configs")]
    [InlineData("/api/v1/admin/finance-methods")]
    [InlineData("/api/v1/admin/goal-templates")]
    [InlineData("/api/v1/admin/survey-questions")]
    public async Task CatalogSearchRunsOnTheServerAndFindsNothingForNonsense(string path)
    {
        var none = await ListAsync($"{path}?search=zzzz-khong-co-gi-khop-9876");

        none.Data.Should().BeEmpty();
        none.Meta!.Total.Should().Be(0);
    }

    [Fact]
    public async Task LimitAboveTheMaximumIsClamped()
    {
        var page = await ListAsync("/api/v1/admin/categories?limit=100000");

        page.Meta!.Limit.Should().Be(100);
    }

    [Fact]
    public async Task SearchIgnoresCaseAndVietnameseAccents()
    {
        var slug = $"thu_{Guid.NewGuid():N}"[..14];
        var create = await SendAsync(HttpMethod.Post, "/api/v1/admin/categories", new { name = "Ăn Thử Ngẫu Nhiên", slug, iconName = "utensils" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var found = await ListAsync("/api/v1/admin/categories?search=AN%20THU%20NGAU");
        var byCode = await ListAsync($"/api/v1/admin/categories?search={slug.ToUpperInvariant()}");

        found.Data.Should().Contain(r => r.GetProperty("slug").GetString() == slug);
        byCode.Data.Should().ContainSingle(r => r.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task CannedRepliesPageAndSearchOnTheServer()
    {
        var tag = $"Zeta{Guid.NewGuid():N}"[..10];
        foreach (var title in new[] { $"{tag}-A", $"{tag}-B", $"{tag}-C" })
        {
            (await SendAsync(HttpMethod.Post, "/api/v1/ops/canned-replies", new { title, body = "Nội dung mẫu để thử tìm kiếm" })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var first = await ListAsync($"/api/v1/ops/canned-replies?includeInactive=true&search={tag}&limit=2&offset=0");
        var second = await ListAsync($"/api/v1/ops/canned-replies?includeInactive=true&search={tag}&limit=2&offset=2");

        first.Meta!.Total.Should().Be(3);
        first.Data.Should().HaveCount(2);
        second.Data.Should().ContainSingle();
        first.Data.Select(IdOf).Intersect(second.Data.Select(IdOf)).Should().BeEmpty();
        (await ListAsync($"/api/v1/ops/canned-replies?includeInactive=true&search={tag}%25")).Meta!.Total.Should().Be(0, "% là chữ, không phải ký tự đại diện");
    }

    [Fact]
    public async Task ApprovalsAndTicketsAcceptPagingAndSearch()
    {
        var approvals = await ListAsync("/api/v1/ops/approvals?limit=5&search=khong-co");
        var tickets = await SendAsync(HttpMethod.Get, "/api/v1/ops/tickets?limit=5&search=khong-co");

        approvals.Meta!.Total.Should().Be(0);
        approvals.Meta.Limit.Should().Be(5);
        tickets.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuditLogsSearchByEventNameAndKeepTheCursorPaging()
    {
        // một thao tác ghi audit chắc chắn có: tạo danh mục
        var slug = $"a_{Guid.NewGuid():N}"[..12];
        await SendAsync(HttpMethod.Post, "/api/v1/admin/categories", new { name = "Audit Thử", slug, iconName = "x" });

        var hit = await ListAsync("/api/v1/admin/audit-logs?search=category&limit=5");
        var miss = await ListAsync("/api/v1/admin/audit-logs?search=zzzz-khong-co-9876");

        hit.Data.Should().NotBeEmpty();
        hit.Data.Should().OnlyContain(r => r.GetProperty("eventType").GetString()!.Contains("category", StringComparison.OrdinalIgnoreCase));
        miss.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task UsersListFiltersInternalAccountsAndTreatsPercentAsText()
    {
        var internalOnly = await ListAsync("/api/v1/admin/users?internalOnly=true&limit=100");
        var percent = await ListAsync("/api/v1/admin/users?search=%25");

        internalOnly.Data.Should().NotBeEmpty();
        internalOnly.Data.Should().OnlyContain(u => u.GetProperty("role").GetString() != "User");
        percent.Data.Should().BeEmpty("% là chữ, không phải ký tự đại diện");
    }

    [Fact]
    public async Task TicketAssigneesAreSearchableAndNeverExposeEmails()
    {
        var found = await ListAsync($"/api/v1/ops/tickets/assignees?search={Uri.EscapeDataString(AuthApiFactory.AdminEmail)}");
        var none = await ListAsync("/api/v1/ops/tickets/assignees?search=khong-co-9876");

        found.Data.Should().NotBeEmpty();
        found.Data.Any(a => a.TryGetProperty("email", out _)).Should().BeFalse("người nhận ca chỉ lộ id, tên, vai trò");
        none.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task DataDeletionRequestsAcceptSearch()
    {
        var page = await ListAsync("/api/v1/admin/data-deletion-requests?search=khong-co-9876");

        page.Data.Should().BeEmpty();
    }
}
