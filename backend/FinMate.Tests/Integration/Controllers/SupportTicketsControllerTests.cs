using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Luồng khiếu nại trên Postgres thật (kiểm luôn migration): người dùng chỉ thấy khiếu nại của mình, nhân sự đi đúng bảng chuyển trạng thái theo vai trò, trợ lý chỉ gợi ý và lỗi của nó không chặn khiếu nại.</summary>
[Collection("Integration")]
public class SupportTicketsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Mine = "/api/v1/support/tickets";
    private const string Queue = "/api/v1/admin/support/tickets";

    private sealed class FakeAi : IAgentAiClient
    {
        public bool Fail { get; set; }

        public Task<AgentGateResult> GateAsync(string text, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AgentPlanResult> PlanAsync(AgentPlanRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AgentComposeResult> ComposeAsync(string question, IReadOnlyList<FinMate.Application.Agent.Evidence> evidence, bool skipVerifier = false, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentMemoryEntry>> GetMemoryAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task PutMemoryAsync(Guid userId, string key, string value, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> DeleteMemoryAsync(Guid userId, string? key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<TicketAssistResult?> TicketAssistAsync(string subject, string body, IReadOnlyList<TicketAssistMessage> history, CancellationToken ct = default)
            => Fail ? throw new AIServiceUnavailableException("sập") : Task.FromResult<TicketAssistResult?>(new TicketAssistResult("Tóm tắt", "Bản nháp", true, null));
    }

    private readonly FakeAi _ai = new();
    private readonly HttpClient _client;

    public SupportTicketsControllerTests(AuthApiFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAgentAiClient>();
            services.AddSingleton<IAgentAiClient>(_ai);
        })).CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record ProfileBody(ProfileData Data);
    private record ProfileData(Guid Id);
    private record Ticket(Guid Id, string Subject, string Status, string? Reply, string? ResolutionNote);
    private record TicketBody(bool Success, Ticket Data);
    private record Page(int Total, List<Ticket> Items);
    private record PageBody(bool Success, Page Data);
    private record AssistData(bool Available, string? Summary, string? SuggestedReply, bool Verified);
    private record AssistBody(AssistData Data);
    private record ErrorData(string Code);
    private record ErrorBody(ErrorData Error);
    private record AuditLogData(string EventType, string? Metadata);
    private record AuditLogListBody(List<AuditLogData> Data);

    private HttpRequestMessage Req(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<string> LoginAsync(string email, string password)
        => (await (await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password))).Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;

    private async Task<string> UserWithRoleAsync(UserRole role)
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Ticket Test User"));
        if (role != UserRole.User)
        {
            var id = (await (await _client.SendAsync(Req(HttpMethod.Get, "/api/v1/users/me", await LoginAsync(email, "Password123!")))).Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data.Id;
            var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
            (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/v1/admin/users/{id}/role", admin, new { role = role.ToString() }))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return await LoginAsync(email, "Password123!");
    }

    private async Task<Guid> CreateAsync(string token, string subject = "Bị trừ tiền hai lần")
    {
        var response = await _client.SendAsync(Req(HttpMethod.Post, Mine, token, new { subject, body = "Mình bị trừ 150.000 đồng hai lần cho cùng một đơn." }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TicketBody>(JsonOptions))!.Data.Id;
    }

    private async Task<HttpStatusCode> PostAsync(string url, string token, object? body = null) => (await _client.SendAsync(Req(HttpMethod.Post, url, token, body ?? new { }))).StatusCode;

    private async Task<Ticket> GetMineAsync(string token, Guid id)
        => (await (await _client.SendAsync(Req(HttpMethod.Get, $"{Mine}/{id}", token))).Content.ReadFromJsonAsync<TicketBody>(JsonOptions))!.Data;

    [Fact]
    public async Task Anonymous_Is401_AndAPlainUserOrContentStaff_Is403_OnTheStaffQueue()
    {
        (await _client.SendAsync(Req(HttpMethod.Post, Mine, null, new { subject = "a", body = "b" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var user = await UserWithRoleAsync(UserRole.User);
        var content = await UserWithRoleAsync(UserRole.ContentStaff);

        foreach (var token in new[] { user, content })
        {
            (await _client.SendAsync(Req(HttpMethod.Get, Queue, token))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task user_sees_only_own_tickets()
    {
        var alice = await UserWithRoleAsync(UserRole.User);
        var bob = await UserWithRoleAsync(UserRole.User);
        var aliceTicket = await CreateAsync(alice, "của Alice");
        await CreateAsync(bob, "của Bob");

        var list = (await (await _client.SendAsync(Req(HttpMethod.Get, Mine, alice))).Content.ReadFromJsonAsync<PageBody>(JsonOptions))!.Data;
        list.Total.Should().Be(1);
        list.Items.Single().Subject.Should().Be("của Alice");
        (await _client.SendAsync(Req(HttpMethod.Get, $"{Mine}/{aliceTicket}", bob))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync($"{Mine}/{aliceTicket}/reopen", bob)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task the_whole_flow_with_an_escalation_runs_on_a_real_database_and_is_audited()
    {
        var user = await UserWithRoleAsync(UserRole.User);
        var support = await UserWithRoleAsync(UserRole.SupportStaff);
        var manager = await UserWithRoleAsync(UserRole.Manager);
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var id = await CreateAsync(user);

        (await PostAsync($"{Queue}/{id}/triage", support)).Should().Be(HttpStatusCode.OK);
        (await PostAsync($"{Queue}/{id}/escalate", support)).Should().Be(HttpStatusCode.OK);
        (await PostAsync($"{Queue}/{id}/review", support)).Should().Be(HttpStatusCode.Forbidden); // chỉ Manager trở lên nhận xử lý
        (await PostAsync($"{Queue}/{id}/review", manager)).Should().Be(HttpStatusCode.OK);
        (await PostAsync($"{Queue}/{id}/decide", support, new { outcome = "resolved", note = "đã hoàn" })).Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync($"{Queue}/{id}/decide", manager, new { outcome = "rejected", note = "Không đủ căn cứ để hoàn tiền." })).Should().Be(HttpStatusCode.OK);

        (await GetMineAsync(user, id)).Status.Should().Be("Rejected");
        (await PostAsync($"{Mine}/{id}/reopen", user)).Should().Be(HttpStatusCode.OK); // người dùng phản đối
        (await GetMineAsync(user, id)).Status.Should().Be("Reopened");

        var audit = (await (await _client.SendAsync(Req(HttpMethod.Get, $"/api/v1/admin/audit-logs?eventType={AuditEvents.SupportTicketTransitioned}&limit=100", admin))).Content.ReadFromJsonAsync<AuditLogListBody>(JsonOptions))!.Data;
        var mine = audit.Where(a => a.Metadata!.Contains(id.ToString())).ToList();
        mine.Should().HaveCount(5); // triage, escalate, review, decide, reopen
        mine.Should().NotContain(a => a.Metadata!.Contains("150.000") || a.Metadata!.Contains("căn cứ"));
    }

    [Fact]
    public async Task an_illegal_step_is_409_with_a_fixed_code_and_changes_nothing()
    {
        var user = await UserWithRoleAsync(UserRole.User);
        var support = await UserWithRoleAsync(UserRole.SupportStaff);
        var id = await CreateAsync(user);

        var response = await _client.SendAsync(Req(HttpMethod.Post, $"{Queue}/{id}/reply", support, new { text = "Mình đã kiểm tra." })); // chưa triage

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be("illegal_ticket_transition");
        (await GetMineAsync(user, id)).Status.Should().Be("Open");
    }

    [Fact]
    public async Task suggested_reply_is_not_sent_automatically_and_a_failing_assistant_does_not_block_the_ticket()
    {
        var user = await UserWithRoleAsync(UserRole.User);
        var support = await UserWithRoleAsync(UserRole.SupportStaff);
        var id = await CreateAsync(user);
        await PostAsync($"{Queue}/{id}/triage", support);

        var assist = (await (await _client.SendAsync(Req(HttpMethod.Post, $"{Queue}/{id}/assist", support))).Content.ReadFromJsonAsync<AssistBody>(JsonOptions))!.Data;
        assist.Should().Be(new AssistData(true, "Tóm tắt", "Bản nháp", true));
        var afterAssist = await GetMineAsync(user, id);
        afterAssist.Status.Should().Be("Triaged"); // gợi ý không tự gửi, không tự đổi trạng thái
        afterAssist.Reply.Should().BeNull();

        _ai.Fail = true;
        var failed = await _client.SendAsync(Req(HttpMethod.Post, $"{Queue}/{id}/assist", support));
        failed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await failed.Content.ReadFromJsonAsync<AssistBody>(JsonOptions))!.Data.Available.Should().BeFalse();

        (await PostAsync($"{Queue}/{id}/reply", support, new { text = "Mình đã kiểm tra giúp bạn." })).Should().Be(HttpStatusCode.OK); // vẫn xử lý được
        (await GetMineAsync(user, id)).Reply.Should().Be("Mình đã kiểm tra giúp bạn.");
        _ai.Fail = false;
    }

    [Fact]
    public async Task the_staff_list_paginates_and_filters_on_the_server()
    {
        var user = await UserWithRoleAsync(UserRole.User);
        var support = await UserWithRoleAsync(UserRole.SupportStaff);
        var unique = $"zz{Guid.NewGuid():N}"[..12];
        await CreateAsync(user, $"{unique} một");
        await CreateAsync(user, $"{unique} hai");
        await CreateAsync(user, "khác hẳn");

        var page = (await (await _client.SendAsync(Req(HttpMethod.Get, $"{Queue}?search={unique}&limit=1&offset=0", support))).Content.ReadFromJsonAsync<PageBody>(JsonOptions))!.Data;

        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(1);
        (await (await _client.SendAsync(Req(HttpMethod.Get, $"{Queue}?status=Resolved&search={unique}", support))).Content.ReadFromJsonAsync<PageBody>(JsonOptions))!.Data.Total.Should().Be(0);
    }

    [Theory]
    [InlineData("", "nội dung", "invalid_ticket_subject")]
    [InlineData("tiêu đề", "", "invalid_ticket_body")]
    public async Task invalid_input_is_422_with_a_fixed_code(string subject, string body, string code)
    {
        var user = await UserWithRoleAsync(UserRole.User);

        var response = await _client.SendAsync(Req(HttpMethod.Post, Mine, user, new { subject, body }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions))!.Error.Code.Should().Be(code);
    }
}
