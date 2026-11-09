using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// API quản trị ĐỊNH DẠNG THÔNG BÁO: chỉ admin, đi qua nguyên văn tới AI Service (client giả ghi lại lời gọi), mỗi lần GHI thành công để lại một dòng audit_logs
/// KHÔNG kèm thân yêu cầu (mẫu có thể chứa chữ thông báo), đọc / thử / thất bại thì không để lại dòng nào.
/// </summary>
[Collection("Integration")]
public class AdminProviderFormatsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid FormatId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private sealed class RecordingClient : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];
        public Func<AiRulesRequest, JsonElement?> Respond { get; set; } = _ => Parse("""{"ok":true}""");

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private readonly RecordingClient _ai = new();
    private readonly HttpClient _client;

    public AdminProviderFormatsControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_ai);
        })).CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AuditLogData(Guid Id, Guid? UserId, string EventType, string? Metadata, DateTimeOffset CreatedAt);
    private record AuditLogListBody(bool Success, List<AuditLogData> Data);

}
