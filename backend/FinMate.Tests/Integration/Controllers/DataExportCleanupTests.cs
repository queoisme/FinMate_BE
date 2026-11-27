using System.Net;
using System.Net.Http.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 91 (BR-EXP-09, 11, 12; S5): hết hạn giữ file, xoá lịch sử, xoá cứng user xoá yêu cầu và file, không xin xoá được khi gói đang dựng.</summary>
[Collection("Integration")]
public class DataExportCleanupTests : IClassFixture<DataExportApiFactory>
{
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportCleanupTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private async Task CleanupAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PersonalDataExportCleanup>().RunAsync();
    }

    private async Task<(LoanTestApi Api, Guid Id)> ReadyExportAsync()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);
        await _factory.RunJobAsync(id);
        return (api, id);
    }

    [Fact]
    public async Task AfterTheRetention_TheExportExpires_TheFileIsDeleted_AndAudited()
    {
        var (api, id) = await ReadyExportAsync();
        var url = (await api.ExpectAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.OK)).GetProperty("url").GetString()!;

        await CleanupAsync();
        (await api.ExportAsync(id)).GetProperty("status").GetString().Should().Be("Ready", "chưa tới hạn");

        try
        {
            _factory.Clock.Shift = TimeSpan.FromHours(72) + TimeSpan.FromMinutes(1);
            await CleanupAsync();
        }
        finally
        {
            _factory.Clock.Shift = TimeSpan.Zero;
        }

        var status = await api.ExportAsync(id);
        status.GetProperty("status").GetString().Should().Be("Expired");
        status.GetProperty("expiredAt").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        (await api.InDbAsync(db => db.PersonalDataExportArchives.CountAsync(a => a.ExportId == id))).Should().Be(0);
        (await api.ErrorCodeAsync(HttpMethod.Get, url, null, HttpStatusCode.Gone)).Should().Be(DataExportErrorCodes.Expired);
        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.Gone)).Should().Be(DataExportErrorCodes.Expired);
        (await api.InDbAsync(db => db.AuditLogs.CountAsync(a => a.UserId == api.UserId && a.EventType == AuditEvents.DataExportExpired))).Should().Be(1);
    }

    [Fact]
    public async Task OldHistoryRows_ArePurged_ButReadyOnesAreKeptUntilTheyExpire()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var old = await api.RequestExportAsync(_factory);
        await api.SetStatusAsync(old, PersonalDataExportStatus.Failed, DataExportFailureCodes.InternalError, counts: false);
        await api.InDbAsync(async db =>
        {
            (await db.PersonalDataExports.SingleAsync(e => e.Id == old)).RequestedAt = DateTimeOffset.UtcNow.AddDays(-400);
            return await db.SaveChangesAsync();
        });
        var current = await api.RequestExportAsync(_factory);
        await _factory.RunJobAsync(current);

        await CleanupAsync();

        (await api.InDbAsync(db => db.PersonalDataExports.Where(e => e.UserId == api.UserId).Select(e => e.Id).ToListAsync())).Should().Equal(current);
    }

    [Fact]
    public async Task HardDeletingTheUser_RemovesTheirRequestsAndFiles()
    {
        var (api, id) = await ReadyExportAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(api.UserId);
        }

        (await api.InDbAsync(db => db.PersonalDataExports.CountAsync(e => e.UserId == api.UserId))).Should().Be(0);
        (await api.InDbAsync(db => db.PersonalDataExportArchives.CountAsync(a => a.ExportId == id))).Should().Be(0);
    }

    [Fact]
    public async Task AskingForDeletion_WhileThePackageIsBeingBuilt_Is409_AndNothingChanges_ThenWorksOnceReady()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);

        (await api.ErrorCodeAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = "Password123!" }, HttpStatusCode.Conflict))
            .Should().Be(DataExportErrorCodes.InProgress);
        (await api.InDbAsync(db => db.DataDeletionRequests.CountAsync(r => r.UserId == api.UserId))).Should().Be(0);
        (await api.InDbAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == api.UserId).Select(u => u.DeletedAt).SingleAsync())).Should().BeNull();

        await _factory.RunJobAsync(id);
        (await api.SendAsync(HttpMethod.Delete, "/api/v1/auth/account", new { password = "Password123!" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = api.Email, password = "Password123!" })).IsSuccessStatusCode
            .Should().BeFalse("tài khoản chờ xoá vẫn không đăng nhập được (BR-EXP-12)");
    }
}
