using System.IO.Compression;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.DataExport;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 91 (BR-EXP-09/10/11): job dựng gói trên Postgres thật — lưu đã mã hoá, Ready, audit, push và email không link, không số liệu.</summary>
[Collection("Integration")]
public class DataExportJobTests : IClassFixture<DataExportApiFactory>
{
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportJobTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [Fact]
    public async Task TheJob_StoresAnEncryptedZip_MarksItReady_AndNotifiesWithoutLinkOrNumbers()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var wallet = await api.WalletAsync();
        await api.TransactionAsync(wallet, null, 123_456, merchant: "Bí mật trong gói");
        var id = await api.RequestExportAsync(_factory);
        _factory.Queue.Enqueued.Should().Contain(id, "yêu cầu mới được đưa vào hàng đợi job");

        await _factory.RunJobAsync(id);

        var status = await api.ExportAsync(id);
        status.GetProperty("status").GetString().Should().Be("Ready");
        var expires = status.GetProperty("expiresAt").GetDateTimeOffset();
        (expires - status.GetProperty("readyAt").GetDateTimeOffset()).Should().Be(TimeSpan.FromHours(72));
        status.GetProperty("sizeBytes").GetInt64().Should().BePositive();

        var archive = await api.InDbAsync(db => db.PersonalDataExportArchives.SingleAsync(a => a.ExportId == id));
        System.Text.Encoding.UTF8.GetString(archive.Data).Should().NotContain("Bí mật trong gói", "lưu đã mã hoá");
        var plain = new SecretCipherExportArchiveProtector(new SecretCipher(DataExportApiFactory.MasterKey)).Unprotect(id, archive.Data, archive.Nonce);
        using (var zip = new ZipArchive(new MemoryStream(plain!)))
        {
            zip.GetEntry("README.txt").Should().NotBeNull();
        }

        var push = _factory.Push.Sent.Where(p => p.UserId == api.UserId).Should().ContainSingle().Subject;
        push.Action.Should().Be(PersonalDataExportNotifier.ReadyAction);
        push.Body.Should().NotContain("http").And.NotContain("123");
        var mail = _factory.Email.BodyFor(api.Email)!;
        mail.Should().Contain("đã sẵn sàng").And.NotContain("http").And.NotContain("123");
        (await api.InDbAsync(db => db.AuditLogs.CountAsync(a => a.UserId == api.UserId && a.EventType == AuditEvents.DataExportReady))).Should().Be(1);
    }

    [Fact]
    public async Task RunningTheJobTwice_DoesNotBuildTwice()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);

        await Task.WhenAll(_factory.RunJobAsync(id), _factory.RunJobAsync(id));
        await _factory.RunJobAsync(id);

        (await api.InDbAsync(db => db.PersonalDataExportArchives.CountAsync(a => a.ExportId == id))).Should().Be(1);
        _factory.Push.Sent.Count(p => p.UserId == api.UserId).Should().Be(1);
    }

    [Fact]
    public async Task AnAccountDeletedWhileWaiting_FailsWithoutNotifying_AndDoesNotCount()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var id = await api.RequestExportAsync(_factory);
        await api.InDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == api.UserId);
            user.DeletedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });

        await _factory.RunJobAsync(id);

        var row = await api.InDbAsync(db => db.PersonalDataExports.SingleAsync(e => e.Id == id));
        row.Status.Should().Be(PersonalDataExportStatus.Failed);
        row.FailureCode.Should().Be(DataExportFailureCodes.AccountDeleted);
        row.CountsTowardLimit.Should().BeFalse();
        _factory.Push.Sent.Should().NotContain(p => p.UserId == api.UserId);
    }
}
