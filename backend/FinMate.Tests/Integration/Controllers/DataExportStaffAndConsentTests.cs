using System.Net;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport.Package;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 91: phía nhân viên (BR-EXP-14: loại ticket mới, câu trả lời mẫu), lịch sử đồng ý từ nay vào gói (D3), và app-config báo gói đầy đủ dùng được (D8).
/// </summary>
[Collection("Integration")]
public class DataExportStaffAndConsentTests : IClassFixture<DataExportApiFactory>
{
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportStaffAndConsentTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private string AdminToken()
    {
        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<FinMateDbContext>().Users.AsNoTracking().Single(u => u.Email == AuthApiFactory.AdminEmail);
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);
    }

    [Fact]
    public async Task ACustomer_CanOpenAPersonalDataRequestTicket_AtP2_AndStaffHaveTheCannedReply()
    {
        await _factory.SetFeaturesAsync(support: true);
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();

        var created = await api.ExpectAsync(HttpMethod.Post, "/api/v1/support/tickets",
            new CreateSupportTicketRequest(SupportTicketCategory.PersonalDataRequest, "Xin dữ liệu của tôi", "Tôi muốn lấy toàn bộ dữ liệu"), HttpStatusCode.Created);

        var ticket = created.GetProperty("ticket");
        ticket.GetProperty("category").GetString().Should().Be("PersonalDataRequest");
        var ticketId = ticket.GetProperty("id").GetGuid();
        (await api.InDbAsync(db => db.SupportTickets.Where(t => t.Id == ticketId).Select(t => t.Priority).SingleAsync())).Should().Be(SupportTicketPriority.P2);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ops/canned-replies");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AdminToken());
        var replies = await (await _client.SendAsync(request)).Content.ReadAsStringAsync();
        replies.Should().Contain("Hướng dẫn tự tải dữ liệu cá nhân").And.Contain("Dữ liệu của tôi");
    }

    [Fact]
    public async Task ConsentChanges_AreAudited_AndShowUpAsHistoryInThePackage()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        await api.ExpectAsync(HttpMethod.Post, "/api/v1/users/me/consents", new { kind = "Training", version = ConsentVersions.Training }, HttpStatusCode.OK);
        await api.ExpectAsync(HttpMethod.Delete, "/api/v1/users/me/consents/training", null, HttpStatusCode.OK);

        var audit = await api.InDbAsync(db => db.AuditLogs.Where(a => a.UserId == api.UserId && a.EventType == AuditEvents.ConsentChanged)
            .OrderBy(a => a.CreatedAt).Select(a => a.Metadata).ToListAsync());
        audit.Should().HaveCount(2);
        var changes = audit.Select(m => JsonDocument.Parse(m!).RootElement).ToList();
        changes[0].GetProperty("type").GetString().Should().Be("Training");
        changes[0].GetProperty("accepted").GetBoolean().Should().BeTrue();
        changes[1].GetProperty("accepted").GetBoolean().Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var sections = await scope.ServiceProvider.GetRequiredService<IPersonalDataReader>().ReadSectionsAsync(api.UserId);
        var consents = JsonSerializer.Serialize(sections.Single(s => s.FileName == "consents.json").Data, PackageJson.Options);
        var history = JsonDocument.Parse(consents).RootElement.GetProperty("history");
        history.GetArrayLength().Should().Be(2);
        history[1].GetProperty("change").GetProperty("accepted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AppConfig_SaysTheFullPackageIsAvailable_WhenTheServerHasTheKey()
    {
        var root = JsonDocument.Parse(await _client.GetStringAsync("/api/v1/app-config")).RootElement.GetProperty("data");

        root.GetProperty("dataExportPackageAvailable").GetBoolean().Should().BeTrue();
        root.GetProperty("config").GetProperty("features").GetProperty("dataExport").GetBoolean().Should().BeTrue();
        root.GetProperty("config").GetProperty("dataExport").GetProperty("csvPerHour").GetInt32().Should().Be(20);
    }
}
