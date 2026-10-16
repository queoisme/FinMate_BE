using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Client chuyển tiếp API quản trị luật: đi qua nguyên văn, kèm người thực hiện dạng băm, và ánh xạ lỗi của AI Service.</summary>
public class AiRulesAdminClientTests
{
    private static readonly Guid Admin = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? Body { get; private set; }
        public string? ActorHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri!.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            ActorHeader = request.Headers.TryGetValues("X-Actor-Hash", out var values) ? values.Single() : null;
            return respond(request);
        }
    }

    private static (AiRulesAdminClient Client, StubHandler Handler) ClientReturning(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new AiRulesAdminClient(new HttpClient(handler) { BaseAddress = new Uri("http://ai-service:8000/") }), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string HashOf(Guid id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString())));

    [Fact]
    public async Task Read_ForwardsPathAndEscapedQuery_WithoutActorHeader()
    {
        var (client, handler) = ClientReturning(_ => Json(HttpStatusCode.OK, """{"items":[],"total":0}"""));

        var result = await client.SendAsync(new AiRulesRequest(
            HttpMethod.Get, "brands", new Dictionary<string, string?> { ["q"] = "bún bò", ["active"] = "true", ["skipped"] = null }));

        handler.Method.Should().Be(HttpMethod.Get);
        handler.PathAndQuery.Should().Be("/api/v1/admin/rules/brands?q=b%C3%BAn%20b%C3%B2&active=true");
        handler.ActorHeader.Should().BeNull();
        result!.Value.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Write_ForwardsBodyVerbatim_AndTheHashedAdminId()
    {
        var (client, handler) = ClientReturning(_ => Json(HttpStatusCode.OK, """{"brand_key":"zeta mart"}"""));
        using var body = JsonDocument.Parse("""{"category_slug":"groceries","display_name":"Zeta"}""");

        await client.SendAsync(new AiRulesRequest(HttpMethod.Put, "brands/zeta%20mart", null, body.RootElement.Clone(), Admin));

        handler.Method.Should().Be(HttpMethod.Put);
        handler.PathAndQuery.Should().Be("/api/v1/admin/rules/brands/zeta%20mart");
        handler.Body.Should().Be("""{"category_slug":"groceries","display_name":"Zeta"}""");
        handler.ActorHeader.Should().Be(HashOf(Admin)).And.NotContain(Admin.ToString());
    }

    [Fact]
    public async Task NoContent_ReturnsNull()
    {
        var (client, _) = ClientReturning(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        (await client.SendAsync(new AiRulesRequest(HttpMethod.Delete, "item-cache", null, null, Admin))).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404, "not_found")]
    [InlineData(HttpStatusCode.Conflict, 409, "duplicate")]
    [InlineData(HttpStatusCode.UnprocessableEntity, 422, "invalid_category")]
    public async Task RuleErrors_KeepTheirStatusCodeAndMessage(HttpStatusCode status, int expected, string code)
    {
        var (client, _) = ClientReturning(_ => Json(status, $$$"""{"error":{"code":"{{{code}}}","message":"Thông điệp {{{code}}}"}}"""));

        var error = (await FluentActions.Awaiting(() => client.SendAsync(new AiRulesRequest(HttpMethod.Post, "keywords", null, null, Admin)))
            .Should().ThrowAsync<AiRuleException>()).Which;

        error.HttpStatusCode.Should().Be(expected);
        error.ErrorCode.Should().Be(code);
        error.Message.Should().Be($"Thông điệp {code}");
    }

    [Fact]
    public async Task FastApiValidationErrors_BecomeA422()
    {
        var (client, _) = ClientReturning(_ => Json(HttpStatusCode.UnprocessableEntity, """{"detail":[{"loc":["header","x-actor-hash"],"msg":"String should have at least 64 characters"}]}"""));

        var error = (await FluentActions.Awaiting(() => client.SendAsync(new AiRulesRequest(HttpMethod.Put, "brands/x", null, null, Admin)))
            .Should().ThrowAsync<AiRuleException>()).Which;

        error.HttpStatusCode.Should().Be(422);
        error.ErrorCode.Should().Be("VALIDATION");
        error.Message.Should().Contain("64 characters");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ServerAndAuthFailures_AreReportedAsServiceUnavailable(HttpStatusCode status)
    {
        var (client, _) = ClientReturning(_ => Json(status, """{"detail":"boom"}"""));

        await FluentActions.Awaiting(() => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "brands")))
            .Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task NetworkFailure_IsReportedAsServiceUnavailable()
    {
        var (client, _) = ClientReturning(_ => throw new HttpRequestException("connection refused"));

        await FluentActions.Awaiting(() => client.SendAsync(new AiRulesRequest(HttpMethod.Get, "brands")))
            .Should().ThrowAsync<AIServiceUnavailableException>();
    }
}
