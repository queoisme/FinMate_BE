using System.IO.Compression;
using System.Net;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 91 (BR-EXP-10, D7, D9): link tải dùng một lần, gắn với user, sống 10 phút, link mới thay link cũ.</summary>
[Collection("Integration")]
public class DataExportDownloadTests : IClassFixture<DataExportApiFactory>
{
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportDownloadTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private async Task<(LoanTestApi Api, Guid Id)> ReadyExportAsync()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);
        await _factory.RunJobAsync(id);
        return (api, id);
    }

    private static async Task<string> LinkAsync(LoanTestApi api, Guid id)
        => (await api.ExpectAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.OK)).GetProperty("url").GetString()!;

    [Fact]
    public async Task ALink_DownloadsTheZipOnce_ThenIsGone()
    {
        var (api, id) = await ReadyExportAsync();
        var url = await LinkAsync(api, id);
        url.Should().StartWith($"{DataExportTestApi.Base}/{id}/download?token=");

        var first = await api.SendAsync(HttpMethod.Get, url);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        first.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex("finmate-du-lieu-\\d{4}-\\d{2}-\\d{2}\\.zip");
        using (var zip = new ZipArchive(new MemoryStream(await first.Content.ReadAsByteArrayAsync())))
        {
            zip.GetEntry("transactions.csv").Should().NotBeNull();
        }

        (await api.ErrorCodeAsync(HttpMethod.Get, url, null, HttpStatusCode.Gone)).Should().Be(DataExportErrorCodes.LinkExpired);
        var status = await api.ExportAsync(id);
        status.GetProperty("downloadCount").GetInt32().Should().Be(1);
        status.GetProperty("lastDownloadedAt").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        (await api.InDbAsync(db => db.AuditLogs.Where(a => a.UserId == api.UserId).Select(a => a.EventType).ToListAsync()))
            .Should().Contain([AuditEvents.DataExportLinkIssued, AuditEvents.DataExportDownloaded]);
    }

    [Fact]
    public async Task ALinkOlderThanTenMinutes_IsGone()
    {
        var (api, id) = await ReadyExportAsync();
        var url = await LinkAsync(api, id);
        try
        {
            _factory.Clock.Shift = TimeSpan.FromMinutes(11);
            (await api.ErrorCodeAsync(HttpMethod.Get, url, null, HttpStatusCode.Gone)).Should().Be(DataExportErrorCodes.LinkExpired);
        }
        finally
        {
            _factory.Clock.Shift = TimeSpan.Zero;
        }

        (await api.SendAsync(HttpMethod.Get, await LinkAsync(api, id))).StatusCode.Should().Be(HttpStatusCode.OK, "vẫn lấy được link mới khi file còn hạn");
    }

    [Fact]
    public async Task ANewLink_ReplacesTheOldOne_AndAMissingTokenIsGone()
    {
        var (api, id) = await ReadyExportAsync();
        var old = await LinkAsync(api, id);
        var fresh = await LinkAsync(api, id);

        (await api.SendAsync(HttpMethod.Get, old)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await api.SendAsync(HttpMethod.Get, $"{DataExportTestApi.Base}/{id}/download")).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await api.SendAsync(HttpMethod.Get, fresh)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnotherUser_Gets404_EvenWithTheRightToken()
    {
        var (owner, id) = await ReadyExportAsync();
        var url = await LinkAsync(owner, id);
        var other = await new LoanTestApi(_factory, _client).SignUpAsync();

        (await other.SendAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.SendAsync(HttpMethod.Get, url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "link gắn với user, không mở được khi chưa đăng nhập");
        (await owner.SendAsync(HttpMethod.Get, url)).StatusCode.Should().Be(HttpStatusCode.OK, "các lần thử trên không đốt link");
    }

    [Fact]
    public async Task BeforeReady_TheLinkIs409_AndAfterExpiry410()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.Conflict))
            .Should().Be(DataExportErrorCodes.NotReady);

        await _factory.RunJobAsync(id);
        await api.SetStatusAsync(id, PersonalDataExportStatus.Expired);
        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.Gone))
            .Should().Be(DataExportErrorCodes.Expired);
    }

    [Fact]
    public async Task TheLinkNeverOutlivesTheFile()
    {
        var (api, id) = await ReadyExportAsync();
        var fileExpiry = DateTimeOffset.UtcNow.AddMinutes(3);
        await api.InDbAsync(async db =>
        {
            var row = await db.PersonalDataExports.SingleAsync(e => e.Id == id);
            row.ExpiresAt = fileExpiry;
            return await db.SaveChangesAsync();
        });

        var link = await api.ExpectAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/{id}/download-link", null, HttpStatusCode.OK);

        link.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(fileExpiry, TimeSpan.FromSeconds(1));
    }
}
