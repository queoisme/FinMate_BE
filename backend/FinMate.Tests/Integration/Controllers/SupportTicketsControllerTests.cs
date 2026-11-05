using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FinMate.API.Controllers.Ops;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Khách chỉ thấy ca của mình và không thấy ghi chú nội bộ; đội vận hành chỉ đọc được nội dung ca thuộc phạm vi của mình.</summary>
[Collection("Integration")]
public class SupportTicketsControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Tickets = "/api/v1/support/tickets";
    private const string Ops = "/api/v1/ops/tickets";
    private const string SecretPhrase = "so-tai-khoan-bi-mat-123";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;
    private string _admin = string.Empty;

    // Fixture dùng chung cho cả lớp, nên chỉ bật cờ một lần: API admin có giới hạn tần suất theo tài khoản.
    private static bool _supportEnabled;

    public SupportTicketsControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Account(string Email, Guid Id, string Token);

    public async Task InitializeAsync()
    {
        _admin = await AdminTokenAsync();
        if (!_supportEnabled)
        {
            await SetSupportEnabledAsync(true);
            _supportEnabled = true;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SetSupportEnabledAsync(bool enabled)
    {
        const string config = "/api/v1/admin/system-config";
        var version = (await DataAsync(await SendAsync(HttpMethod.Get, config, _admin))).GetProperty("version").GetInt32();
        var saved = await SendAsync(HttpMethod.Put, config, _admin, new { expectedVersion = version, content = new { features = new { support = enabled } }, note = "kiem thu" });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Tạo tài khoản và token trực tiếp thay vì qua API: bộ test này cần rất nhiều tài khoản, gọi đăng ký và đăng nhập sẽ đụng giới hạn tần suất theo IP.
    private async Task<string> AdminTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);
    }

    private async Task<Account> AccountAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@finmate.local",
            DisplayName = role == UserRole.User ? "Khách" : "Nhân viên",
            Role = role,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Email, user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private Task<Account> CustomerAsync() => AccountAsync(UserRole.User);

    private Task<Account> OpsAsync(UserRole role) => AccountAsync(role);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<(Guid TicketId, Guid MessageId)> CreateTicketAsync(Account customer, SupportTicketCategory category = SupportTicketCategory.Receipt)
    {
        var data = await DataAsync(await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(category, "AI không đọc được hóa đơn", "Hóa đơn của tôi " + SecretPhrase)));
        return (data.GetProperty("ticket").GetProperty("id").GetGuid(), data.GetProperty("messages")[0].GetProperty("id").GetGuid());
    }

    private HttpClient ClientWithCipher()
        => _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<SecretCipher>();
            services.AddSingleton(new SecretCipher(Convert.ToBase64String(new byte[32])));
        })).CreateClient();

    private static HttpRequestMessage Upload(string url, string token, byte[] content, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "anh.png");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task CustomerApiIsOffWhileTheFlagIsOff_ButOpsCanStillWork()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        await SetSupportEnabledAsync(false);
        try
        {
            var refused = await SendAsync(HttpMethod.Get, Tickets, customer.Token);
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ErrorCodeAsync(refused)).Should().Be(SupportErrorCodes.Disabled);
            (await SendAsync(HttpMethod.Get, Ops, staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetSupportEnabledAsync(true);
        }
    }

    [Theory]
    [InlineData(SupportTicketCategory.Security, "P1")]
    [InlineData(SupportTicketCategory.Receipt, "P2")]
    [InlineData(SupportTicketCategory.Question, "P3")]
    public async Task PriorityAndDueDatesComeFromTheCategory(SupportTicketCategory category, string expected)
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer, category);

        var ticket = (await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", staff.Token))).GetProperty("ticket");

        ticket.GetProperty("priority").GetString().Should().Be(expected);
        ticket.GetProperty("firstResponseDueAt").GetDateTimeOffset().Should().BeBefore(ticket.GetProperty("resolveDueAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task CustomerCannotSeeOrTouchAnotherCustomersTicket()
    {
        var owner = await CustomerAsync();
        var other = await CustomerAsync();
        var (ticketId, messageId) = await CreateTicketAsync(owner);

        (await SendAsync(HttpMethod.Get, $"{Tickets}/{ticketId}", other.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/messages", other.Token, new SupportMessageRequest("xen vào"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/close", other.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.SendAsync(Upload($"{Tickets}/{ticketId}/messages/{messageId}/attachments", other.Token, Png, "image/png"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DataAsync(await SendAsync(HttpMethod.Get, Tickets, other.Token))).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task OrdinaryUserCannotReachTheOpsTicketApi()
    {
        var customer = await CustomerAsync();
        var (ticketId, _) = await CreateTicketAsync(customer);

        (await SendAsync(HttpMethod.Get, Ops, customer.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", customer.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InternalNotesAndStaffFieldsNeverReachTheCustomer()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));

        const string note = "ghi-chu-noi-bo-khong-duoc-lo";
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", staff.Token, new OpsTicketMessageRequest(note, true)));
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", staff.Token, new OpsTicketMessageRequest("Chào bạn", false)));

        var customerView = await (await SendAsync(HttpMethod.Get, $"{Tickets}/{ticketId}", customer.Token)).Content.ReadAsStringAsync();
        customerView.Should().NotContain(note).And.Contain("Chào bạn");
        customerView.Should().NotContain("assignee", because: "khách không cần biết nhân viên nào phụ trách");
        customerView.Should().NotContain("authorUserId");
        customerView.Should().NotContain("DueAt");

        var staffView = await (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", staff.Token)).Content.ReadAsStringAsync();
        staffView.Should().Contain(note);
    }

    [Fact]
    public async Task StaffCannotHandleTheirOwnTicket_AndDoesNotSeeItInTheQueue()
    {
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(staff);

        (await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var queue = await (await SendAsync(HttpMethod.Get, $"{Ops}?limit=100", staff.Token)).Content.ReadAsStringAsync();
        queue.Should().NotContain(ticketId.ToString());
    }

    [Fact]
    public async Task ManagerCannotAssignATicketToItsOwnRequester()
    {
        var manager = await OpsAsync(UserRole.OpsManager);
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(staff);

        var response = await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/assignee", manager.Token, new OpsTicketAssignRequest(staff.Id));

        (await ErrorCodeAsync(response)).Should().Be(SupportErrorCodes.InvalidAssignee);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentClaimsWins()
    {
        var customer = await CustomerAsync();
        var first = await OpsAsync(UserRole.Staff);
        var second = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", first.Token),
            SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", second.Token));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    }

    [Fact]
    public async Task StaffOnlyReadsAndActsOnTicketsInTheirScope()
    {
        var customer = await CustomerAsync();
        var handler = await OpsAsync(UserRole.Staff);
        var other = await OpsAsync(UserRole.Staff);
        var manager = await OpsAsync(UserRole.OpsManager);
        var (ticketId, _) = await CreateTicketAsync(customer);

        // Ca chưa ai nhận: đọc được để quyết định nhận, nhưng chưa nhận thì chưa trả lời được.
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", other.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", other.Token, new OpsTicketMessageRequest("x", false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", handler.Token));

        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", other.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", other.Token, new OpsTicketMessageRequest("x", false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/status", other.Token, new OpsTicketStatusRequest(SupportTicketStatus.Closed))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", manager.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OnlyManagersReassignAndChangePriority()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var manager = await OpsAsync(UserRole.OpsManager);
        var (ticketId, _) = await CreateTicketAsync(customer);

        (await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/assignee", staff.Token, new OpsTicketAssignRequest(staff.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/priority", staff.Token, new OpsTicketPriorityRequest(SupportTicketPriority.P1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var assigned = (await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/assignee", manager.Token, new OpsTicketAssignRequest(staff.Id)))).GetProperty("ticket");
        assigned.GetProperty("assigneeUserId").GetGuid().Should().Be(staff.Id);
        assigned.GetProperty("status").GetString().Should().Be("InProgress");

        var before = assigned.GetProperty("resolveDueAt").GetDateTimeOffset();
        var raised = (await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/priority", manager.Token, new OpsTicketPriorityRequest(SupportTicketPriority.P1)))).GetProperty("ticket");
        raised.GetProperty("priority").GetString().Should().Be("P1");
        raised.GetProperty("resolveDueAt").GetDateTimeOffset().Should().BeBefore(before);
    }

    [Fact]
    public async Task ManagerCannotAssignToAnOrdinaryUser()
    {
        var customer = await CustomerAsync();
        var manager = await OpsAsync(UserRole.OpsManager);
        var (ticketId, _) = await CreateTicketAsync(customer);

        var response = await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/assignee", manager.Token, new OpsTicketAssignRequest(customer.Id));

        (await ErrorCodeAsync(response)).Should().Be(SupportErrorCodes.InvalidAssignee);
    }

    [Fact]
    public async Task LifecycleFollowsWhoseTurnItIs()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer);

        async Task<JsonElement> OpsTicketAsync() => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}", staff.Token))).GetProperty("ticket");

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));
        (await OpsTicketAsync()).GetProperty("firstRespondedAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Ghi chú nội bộ không tính là đã phản hồi khách.
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", staff.Token, new OpsTicketMessageRequest("note", true)));
        (await OpsTicketAsync()).GetProperty("firstRespondedAt").ValueKind.Should().Be(JsonValueKind.Null);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", staff.Token, new OpsTicketMessageRequest("Bạn gửi lại ảnh nhé", false)));
        var waiting = await OpsTicketAsync();
        waiting.GetProperty("status").GetString().Should().Be("WaitingCustomer");
        waiting.GetProperty("firstRespondedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/messages", customer.Token, new SupportMessageRequest("Đây ạ")));
        (await OpsTicketAsync()).GetProperty("status").GetString().Should().Be("InProgress");

        // Chưa giải quyết thì chưa chấm điểm được.
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/rating", customer.Token, new SupportRatingRequest(5)))).Should().Be(SupportErrorCodes.RatingNotAllowed);

        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/status", staff.Token, new OpsTicketStatusRequest(SupportTicketStatus.Resolved)));
        (await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/rating", customer.Token, new SupportRatingRequest(5)))).GetProperty("rating").GetInt32().Should().Be(5);

        // Khách thấy chưa xong thì trả lời là mở lại.
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/messages", customer.Token, new SupportMessageRequest("Vẫn sai")));
        var reopened = await OpsTicketAsync();
        reopened.GetProperty("status").GetString().Should().Be("InProgress");
        reopened.GetProperty("resolvedAt").ValueKind.Should().Be(JsonValueKind.Null);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/close", customer.Token));
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{ticketId}/messages", customer.Token, new SupportMessageRequest("nữa")))).Should().Be(SupportErrorCodes.TicketClosed);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/messages", staff.Token, new OpsTicketMessageRequest("nữa", false)))).Should().Be(SupportErrorCodes.TicketClosed);
    }

    [Fact]
    public async Task EscalationRequiresSayingWhatWasTried()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/escalate", staff.Token, new OpsTicketEscalateRequest("  ")))).Should().Be(SupportErrorCodes.InvalidInput);

        var escalated = await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/escalate", staff.Token, new OpsTicketEscalateRequest("Đã thử quét lại")));
        escalated.GetProperty("ticket").GetProperty("status").GetString().Should().Be("Escalated");
    }

    [Fact]
    public async Task OpenTicketLimitIsEnforced()
    {
        var customer = await CustomerAsync();
        for (var i = 0; i < 10; i++)
        {
            await CreateTicketAsync(customer);
        }

        var refused = await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(SupportTicketCategory.Question, "a", "b"));
        (await ErrorCodeAsync(refused)).Should().Be(SupportErrorCodes.TooManyOpenTickets);
    }

    [Fact]
    public async Task QueueFiltersByAssigneeAndOverdue()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (mine, _) = await CreateTicketAsync(customer);
        var (free, _) = await CreateTicketAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{mine}/claim", staff.Token));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.SupportTickets.Where(t => t.Id == free).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResolveDueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        async Task<string> QueueAsync(string query) => await (await SendAsync(HttpMethod.Get, $"{Ops}?limit=100&{query}", staff.Token)).Content.ReadAsStringAsync();

        var meQueue = await QueueAsync("assignee=me");
        meQueue.Should().Contain(mine.ToString()).And.NotContain(free.ToString());

        var noneQueue = await QueueAsync("assignee=none");
        noneQueue.Should().Contain(free.ToString()).And.NotContain(mine.ToString());

        var overdue = await QueueAsync("overdue=true");
        overdue.Should().Contain(free.ToString()).And.NotContain(mine.ToString());
    }

    [Fact]
    public async Task AttachmentsAreEncryptedAtRestAndOnlyOpenToTheOwnerAndTheHandler()
    {
        var client = ClientWithCipher();
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var bystander = await OpsAsync(UserRole.Staff);
        var (ticketId, messageId) = await CreateTicketAsync(customer);

        var uploaded = await client.SendAsync(Upload($"{Tickets}/{ticketId}/messages/{messageId}/attachments", customer.Token, Png, "image/png"));
        var attachmentId = (await DataAsync(uploaded)).GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().SupportTicketAttachments.SingleAsync(a => a.Id == attachmentId);
            row.Data.Should().NotContainInOrder(Png);
            row.ContentType.Should().Be("image/png");
            row.DeleteAfter.Should().BeNull();
        }

        async Task<HttpResponseMessage> GetAsync(string url, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request);
        }

        var own = await GetAsync($"{Tickets}/{ticketId}/attachments/{attachmentId}", customer.Token);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        own.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");

        (await GetAsync($"{Tickets}/{ticketId}/attachments/{attachmentId}", other.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Phải nhận ca rồi mới xem được ảnh, kể cả khi ca chưa ai nhận.
        (await GetAsync($"{Ops}/{ticketId}/attachments/{attachmentId}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));
        (await GetAsync($"{Ops}/{ticketId}/attachments/{attachmentId}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync($"{Ops}/{ticketId}/attachments/{attachmentId}", bystander.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var logs = await (await SendAsync(HttpMethod.Get, "/api/v1/admin/audit-logs?eventType=Ops.Ticket.AttachmentViewed&limit=100", _admin)).Content.ReadAsStringAsync();
        logs.Should().Contain(attachmentId.ToString());
    }

    [Fact]
    public async Task AFileDisguisedAsAnImageIsRefused()
    {
        var client = ClientWithCipher();
        var customer = await CustomerAsync();
        var (ticketId, messageId) = await CreateTicketAsync(customer);

        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var response = await client.SendAsync(Upload($"{Tickets}/{ticketId}/messages/{messageId}/attachments", customer.Token, html, "image/png"));

        (await ErrorCodeAsync(response)).Should().Be(SupportErrorCodes.AttachmentInvalid);
    }

    [Fact]
    public async Task AttachmentLimitPerMessageIsEnforced()
    {
        var client = ClientWithCipher();
        var customer = await CustomerAsync();
        var (ticketId, messageId) = await CreateTicketAsync(customer);
        var url = $"{Tickets}/{ticketId}/messages/{messageId}/attachments";

        for (var i = 0; i < 3; i++)
        {
            await DataAsync(await client.SendAsync(Upload(url, customer.Token, Png, "image/png")));
        }

        (await ErrorCodeAsync(await client.SendAsync(Upload(url, customer.Token, Png, "image/png")))).Should().Be(SupportErrorCodes.TooManyAttachments);
    }

    [Fact]
    public async Task WithoutAMasterKeyImagesAreRefusedRatherThanStoredInPlainText()
    {
        var customer = await CustomerAsync();
        var (ticketId, messageId) = await CreateTicketAsync(customer);

        var response = await _client.SendAsync(Upload($"{Tickets}/{ticketId}/messages/{messageId}/attachments", customer.Token, Png, "image/png"));

        (await ErrorCodeAsync(response)).Should().Be(SupportErrorCodes.AttachmentsUnavailable);
    }

    [Fact]
    public async Task RetentionClosesStaleResolvedTicketsAndDeletesExpiredImages()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<SecretCipher>();
            services.AddSingleton(new SecretCipher(Convert.ToBase64String(new byte[32])));
        }));
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, messageId) = await CreateTicketAsync(customer);
        await DataAsync(await factory.CreateClient().SendAsync(Upload($"{Tickets}/{ticketId}/messages/{messageId}/attachments", customer.Token, Png, "image/png")));
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));
        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{ticketId}/status", staff.Token, new OpsTicketStatusRequest(SupportTicketStatus.Resolved)));

        using var scope = factory.Services.CreateScope();
        var retention = scope.ServiceProvider.GetRequiredService<ISupportRetentionService>();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();

        // Mới giải quyết: chưa đóng, ảnh còn.
        await retention.RunAsync(DateTimeOffset.UtcNow);
        (await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticketId)).Status.Should().Be(SupportTicketStatus.Resolved);

        // Quá 7 ngày: tự đóng và đặt hạn xóa ảnh 30 ngày.
        var closedAt = DateTimeOffset.UtcNow.AddDays(8);
        await retention.RunAsync(closedAt);
        (await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticketId)).Status.Should().Be(SupportTicketStatus.Closed);
        var image = await db.SupportTicketAttachments.AsNoTracking().SingleAsync(a => a.TicketId == ticketId);
        image.DeleteAfter.Should().BeCloseTo(closedAt.AddDays(30), TimeSpan.FromSeconds(1));

        await retention.RunAsync(closedAt.AddDays(29));
        (await db.SupportTicketAttachments.AsNoTracking().CountAsync(a => a.TicketId == ticketId)).Should().Be(1);

        await retention.RunAsync(closedAt.AddDays(31));
        (await db.SupportTicketAttachments.AsNoTracking().CountAsync(a => a.TicketId == ticketId)).Should().Be(0);

        // Tin nhắn vẫn còn, chỉ mất ảnh.
        (await db.SupportTicketMessages.AsNoTracking().CountAsync(m => m.TicketId == ticketId)).Should().Be(1);
    }

    [Fact]
    public async Task HardDeletingACustomerRemovesTheirTickets_HardDeletingStaffKeepsTheTicket()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var customer2 = await CustomerAsync();
        var (gone, _) = await CreateTicketAsync(customer);
        var (kept, _) = await CreateTicketAsync(customer2);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{kept}/claim", staff.Token));
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{kept}/messages", staff.Token, new OpsTicketMessageRequest("Chào", false)));

        using var scope = _factory.Services.CreateScope();
        var deleter = scope.ServiceProvider.GetRequiredService<IUserHardDeleter>();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();

        await deleter.HardDeleteAsync(customer.Id);
        await deleter.HardDeleteAsync(staff.Id);

        (await db.SupportTickets.AsNoTracking().AnyAsync(t => t.Id == gone)).Should().BeFalse();
        (await db.SupportTicketMessages.AsNoTracking().AnyAsync(m => m.TicketId == gone)).Should().BeFalse();

        var remaining = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == kept);
        remaining.AssigneeUserId.Should().BeNull();
        (await db.SupportTicketMessages.AsNoTracking().CountAsync(m => m.TicketId == kept)).Should().Be(2);
    }

    [Fact]
    public async Task AuditLogNeverContainsTicketContent()
    {
        var customer = await CustomerAsync();
        var staff = await OpsAsync(UserRole.Staff);
        var (ticketId, _) = await CreateTicketAsync(customer);
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/escalate", staff.Token, new OpsTicketEscalateRequest(SecretPhrase)));

        var logs = await (await SendAsync(HttpMethod.Get, "/api/v1/admin/audit-logs?limit=100", _admin)).Content.ReadAsStringAsync();

        logs.Should().Contain("Ops.Ticket.Escalated").And.NotContain(SecretPhrase);
    }
}
