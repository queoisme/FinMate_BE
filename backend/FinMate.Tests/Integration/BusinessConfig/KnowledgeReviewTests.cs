using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>
/// Phase 95 §8 + S3: backend ghi người gửi tài liệu tri thức (AI Service không nhận id), người duyệt / từ chối khác người gửi, quyền duyệt đọc từ DB,
/// tài liệu mới không có dòng ghi thì không duyệt được, và ghi hỏng sau khi AI Service đã nhận thì tài liệu bị gỡ và người gửi nhận lỗi.
/// AI Service là bản giả trả lời theo đường dẫn.
/// </summary>
[Collection("Integration")]
public class KnowledgeReviewTests : IClassFixture<AuthApiFactory>
{
    private const string Base = "/api/v1/admin/knowledge";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class FakeAi : IAiRulesAdminClient
    {
        public List<AiRulesRequest> Calls { get; } = [];

        /// <summary>id tài liệu → created_at mà <c>GET documents/{id}</c> trả về.</summary>
        public Dictionary<Guid, DateTimeOffset> CreatedAt { get; } = [];

        public Guid NextId { get; set; } = Guid.NewGuid();

        public Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default)
        {
            lock (Calls)
            {
                Calls.Add(request);
            }

            string json;
            if (request.Method == HttpMethod.Post && request.Path == "documents")
            {
                json = $$"""{"id":"{{NextId}}","status":"Draft","chunks":2}""";
            }
            else if (request.Method == HttpMethod.Get && request.Path == "documents")
            {
                var items = string.Join(",", CreatedAt.Keys.Select(id => $$"""{"id":"{{id}}","title":"t","status":"Draft"}"""));
                json = $$"""{"total":{{CreatedAt.Count}},"items":[{{items}}]}""";
            }
            else if (request.Method == HttpMethod.Get && request.Path.StartsWith("documents/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(request.Path["documents/".Length..]);
                var created = CreatedAt.TryGetValue(id, out var at) ? $$""","created_at":"{{at:O}}" """ : string.Empty;
                json = $$"""{"id":"{{id}}","status":"Draft"{{created}}}""";
            }
            else
            {
                json = """{"ok":true}""";
            }

            return Task.FromResult<JsonElement?>(JsonDocument.Parse(json).RootElement.Clone());
        }
    }

    /// <summary>Kho ghi người gửi hỏng ở lần thêm: mô phỏng DB lỗi ngay sau khi AI Service đã nhận tài liệu.</summary>
    private sealed class FailingReviews(IKnowledgeReviewRepository inner) : IKnowledgeReviewRepository
    {
        public Task<KnowledgeReview?> GetAsync(Guid documentId, CancellationToken ct = default) => inner.GetAsync(documentId, ct);
        public Task<IReadOnlyDictionary<Guid, KnowledgeReview>> GetManyAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default) => inner.GetManyAsync(documentIds, ct);
        public Task AddAsync(KnowledgeReview review, CancellationToken ct = default) => throw new DbUpdateException("giả lập DB lỗi");
        public Task SaveChangesAsync(CancellationToken ct = default) => inner.SaveChangesAsync(ct);
        public Task<DateTimeOffset?> GetTrackedSinceAsync(CancellationToken ct = default) => inner.GetTrackedSinceAsync(ct);
    }

    private readonly AuthApiFactory _base;
    private readonly FakeAi _ai = new();

    public KnowledgeReviewTests(AuthApiFactory factory)
    {
        _base = factory;
    }

    private WebApplicationFactory<FinMate.API.Program> Factory(bool failingReviews = false)
        => _base.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiRulesAdminClient>();
            services.AddSingleton<IAiRulesAdminClient>(_ai);
            if (failingReviews)
            {
                services.AddScoped<FinMate.Infrastructure.Persistence.Repositories.KnowledgeReviewRepository>();
                services.RemoveAll<IKnowledgeReviewRepository>();
                services.AddScoped<IKnowledgeReviewRepository>(sp => new FailingReviews(sp.GetRequiredService<FinMate.Infrastructure.Persistence.Repositories.KnowledgeReviewRepository>()));
            }
        }));

    private static async Task<(Guid Id, string Token)> AccountAsync(WebApplicationFactory<FinMate.API.Program> factory, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"kb{Guid.NewGuid():N}@finmate.local", DisplayName = "Nhân sự", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return client.SendAsync(request);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static async Task<KnowledgeReview?> ReviewAsync(WebApplicationFactory<FinMate.API.Program> factory, Guid documentId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().KnowledgeReviews.AsNoTracking().SingleOrDefaultAsync(r => r.DocumentId == documentId);
    }

    private async Task<Guid> SubmitAsync(HttpClient client, string token)
    {
        _ai.NextId = Guid.NewGuid();
        var response = await SendAsync(client, HttpMethod.Post, $"{Base}/documents", token, new { title = "Quy tắc 50/30/20", source_name = "FinMate", as_of = "2026-11-01", effective_from = "2026-11-01", text = "Nội dung" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return _ai.NextId;
    }

    [Fact]
    public async Task TheAuthorIsRecorded_AndOnlySomeoneElseWithTheCapabilityDecides()
    {
        var factory = Factory();
        var client = factory.CreateClient();
        var manager = await AccountAsync(factory, UserRole.OpsManager);
        var staff = await AccountAsync(factory, UserRole.Staff);

        var id = await SubmitAsync(client, manager.Token);
        (await ReviewAsync(factory, id))!.SubmittedBy.Should().Be(manager.Id);

        var own = await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/approve", manager.Token);
        own.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(own)).Should().Be("KNOWLEDGE_OWN_DRAFT");
        (await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/approve", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Staff không duyệt");

        var other = await AccountAsync(factory, UserRole.Admin);
        (await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/approve", other.Token)).StatusCode.Should().Be(HttpStatusCode.OK, "S5: Admin duyệt bản của người khác");
        var review = await ReviewAsync(factory, id);
        review!.Status.Should().Be(KnowledgeReviewStatus.Approved);
        review.DecidedBy.Should().Be(other.Id);
    }

    [Fact]
    public async Task RejectingNeedsANote_RetiresTheDraftAtTheAiService_AndKeepsTheNoteOutOfTheAudit()
    {
        var factory = Factory();
        var client = factory.CreateClient();
        var staff = await AccountAsync(factory, UserRole.Staff);
        var manager = await AccountAsync(factory, UserRole.OpsManager);
        var id = await SubmitAsync(client, staff.Token);

        (await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/reject", manager.Token, new { note = "" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var rejected = await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/reject", manager.Token, new { note = "nguồn chưa đủ tin cậy" });

        rejected.StatusCode.Should().Be(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        lock (_ai.Calls)
        {
            _ai.Calls.Should().Contain(c => c.Method == HttpMethod.Post && c.Path == $"documents/{id}/retire");
        }

        var review = await ReviewAsync(factory, id);
        review!.Status.Should().Be(KnowledgeReviewStatus.Rejected);
        review.DecisionNote.Should().Be("nguồn chưa đủ tin cậy");
        using var scope = factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.UserId == manager.Id && a.EventType == "Admin.Knowledge.Rejected").ToListAsync();
        audit.Should().ContainSingle().Which.Metadata.Should().NotContain("nguồn chưa đủ tin cậy");
    }

    [Fact]
    public async Task S3_ADocumentCreatedAfterTheCutoverWithoutARecordCannotBeApproved_AnOlderOneCan()
    {
        var factory = Factory();
        var client = factory.CreateClient();
        var manager = await AccountAsync(factory, UserRole.OpsManager);
        var fresh = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        _ai.CreatedAt[fresh] = DateTimeOffset.UtcNow.AddMinutes(1);
        _ai.CreatedAt[legacy] = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var missing = await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{fresh}/approve", manager.Token);
        missing.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(missing)).Should().Be("KNOWLEDGE_REVIEW_MISSING");
        (await ReviewAsync(factory, fresh)).Should().BeNull();

        (await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{legacy}/approve", manager.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = await ReviewAsync(factory, legacy);
        recorded!.SubmittedBy.Should().BeNull("tài liệu cũ không biết người gửi");
        recorded.Status.Should().Be(KnowledgeReviewStatus.Approved);
        recorded.DecidedBy.Should().Be(manager.Id);
    }

    [Fact]
    public async Task S3_AFailedRecordAfterTheAiAcceptedRetiresTheDocument_AndTellsTheAuthorToRetry()
    {
        var factory = Factory(failingReviews: true);
        var client = factory.CreateClient();
        var staff = await AccountAsync(factory, UserRole.Staff);
        _ai.NextId = Guid.NewGuid();

        var response = await SendAsync(client, HttpMethod.Post, $"{Base}/documents", staff.Token, new { title = "x", source_name = "y", as_of = "2026-11-01", effective_from = "2026-11-01", text = "z" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be("KNOWLEDGE_SUBMIT_FAILED");
        lock (_ai.Calls)
        {
            _ai.Calls.Should().Contain(c => c.Method == HttpMethod.Post && c.Path == $"documents/{_ai.NextId}/retire", "tài liệu mồ côi được gỡ ngay");
        }
    }

    [Fact]
    public async Task TheListShowsWhoSubmittedAndTheReviewStatus()
    {
        var factory = Factory();
        var client = factory.CreateClient();
        var staff = await AccountAsync(factory, UserRole.Staff);
        var id = await SubmitAsync(client, staff.Token);
        _ai.CreatedAt[id] = DateTimeOffset.UtcNow;

        var list = JsonDocument.Parse(await (await SendAsync(client, HttpMethod.Get, $"{Base}/documents", staff.Token)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("items");

        var item = list.EnumerateArray().Single(i => i.GetProperty("id").GetString() == id.ToString());
        item.GetProperty("submitted_by").GetString().Should().Be(staff.Id.ToString());
        item.GetProperty("review_status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task RemovingTheCapabilityStopsTheSameTokenAtOnce()
    {
        var factory = Factory();
        var client = factory.CreateClient();
        var staff = await AccountAsync(factory, UserRole.Staff);
        var manager = await AccountAsync(factory, UserRole.OpsManager);
        var admin = await AccountAsync(factory, UserRole.Admin);
        var id = await SubmitAsync(client, staff.Token);
        var defaults = OpsPermissions.Names(OpsPermissions.For(UserRole.OpsManager));
        (await SendAsync(client, HttpMethod.Put, "/api/v1/admin/role-permissions/OpsManager", admin.Token,
            new { capabilities = defaults.Where(c => c != nameof(OpsCapability.ApproveAgentContent)).ToList(), reason = "kiểm thử" })).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            (await SendAsync(client, HttpMethod.Post, $"{Base}/documents/{id}/approve", manager.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await SendAsync(client, HttpMethod.Put, "/api/v1/admin/role-permissions/OpsManager", admin.Token, new { capabilities = defaults, reason = "trả lại" });
        }
    }
}
