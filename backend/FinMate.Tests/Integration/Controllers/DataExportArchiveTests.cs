using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport.Package;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 91 (BR-EXP-07/08): dựng ZIP trên Postgres thật rồi đọc lại. Đủ file theo danh mục, JSON hợp lệ, và KHÔNG có chuỗi bí mật hay dữ liệu
/// người khác: mật khẩu băm, refresh token, token FCM, nội dung thông báo thô, ghi chú nội bộ, tên/email/giao dịch của thành viên khác (kịch bản d).
/// </summary>
[Collection("Integration")]
public class DataExportArchiveTests : IClassFixture<DataExportApiFactory>
{
    private static readonly string[] ExpectedFiles =
    [
        "README.txt", "account_requests.json", "accounts.json", "agent_memory.json", "anomalies.json", "bank_notifications.json", "budgets.json",
        "categories.json", "category_rules.json", "community.json", "consents.json", "devices.json", "finance_methods.json", "forecasts.json",
        "goals.json", "groups.json", "insights.json", "loans.json", "notification_settings.json", "notifications.json", "profile.json",
        "qr_transfers.json", "receipts.json", "recurring_items.json", "security_log.json", "subscriptions.json", "support.json",
        "transactions.csv", "transactions_deleted.csv",
    ];

    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportArchiveTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.Agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentMemoryEntry("preferred_name", "Thiện", DateTimeOffset.UtcNow)]);
    }

    private async Task<Dictionary<string, string>> BuildAsync(Guid userId, PersonalDataArchiveBuilder? builder = null)
    {
        using var scope = _factory.Services.CreateScope();
        builder ??= scope.ServiceProvider.GetRequiredService<PersonalDataArchiveBuilder>();
        using var output = new MemoryStream();
        var result = await builder.BuildAsync(userId, output, 25L * 1024 * 1024, DateTimeOffset.UtcNow);
        result.SizeBytes.Should().Be(output.Length);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    private static T Insert<T>(FinMateDbContext db, T entity) where T : class
    {
        db.Add(entity);
        return entity;
    }

    [Fact]
    public async Task TheZip_HasEveryFile_ValidJson_AndAReadme()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var wallet = await api.WalletAsync("Ví chính");
        await api.TransactionAsync(wallet, await api.CategoryAsync("food"), 45_000, merchant: "Phở Thìn");

        var files = await BuildAsync(api.UserId);

        files.Keys.Order(StringComparer.Ordinal).Should().Equal(ExpectedFiles);
        foreach (var (name, content) in files.Where(f => f.Key.EndsWith(".json")))
        {
            var root = JsonDocument.Parse(content).RootElement;
            root.GetProperty("formatVersion").GetInt32().Should().Be(1, name);
            root.GetProperty("exportedAt").GetString().Should().EndWith("+07:00", name);
            (root.TryGetProperty("items", out _) || root.TryGetProperty("data", out _)).Should().BeTrue(name);
        }

        files["README.txt"].Should().Contain("đồng (VND)").And.Contain("UTF+07:00".Replace("UTF", "UTC")).And.Contain("Phiên bản định dạng: 1")
            .And.Contain("From Text/CSV").And.Contain("Google Sheets").And.Contain("mã băm");
        files["transactions.csv"].Should().Contain("Phở Thìn");
        files["profile.json"].Should().Contain(api.Email);
        files["agent_memory.json"].Should().Contain("preferred_name");
    }

    [Fact]
    public async Task NoSecret_AndNothingWrittenByOrAboutAnotherMember_EndsUpInTheZip()
    {
        var a = await new LoanTestApi(_factory, _client).SignUpAsync();
        var b = await new LoanTestApi(_factory, _client).SignUpAsync();
        var aWallet = await a.WalletAsync("Ví A");
        var bWallet = await b.WalletAsync("Ví B");
        var bShared = await b.TransactionAsync(bWallet, null, 777_000, merchant: "Nhà hàng riêng của B");
        var secrets = await SeedSecretsAsync(a, b, aWallet, bShared);

        var files = await BuildAsync(a.UserId);
        var everything = string.Join("\n", files.Values);

        secrets.Where(s => everything.Contains(s, StringComparison.Ordinal)).Should().BeEmpty("không chuỗi bí mật hay dữ liệu người khác nào được lọt vào gói");

        var groups = JsonDocument.Parse(files["groups.json"]).RootElement.GetProperty("data").GetProperty("groups");
        groups.GetArrayLength().Should().Be(1);
        groups[0].GetProperty("members").EnumerateArray().Select(m => m.GetProperty("role").GetString()).Should().BeEquivalentTo("Owner", "Member");
        groups[0].GetProperty("bills")[0].GetProperty("yourShareCents").GetInt64().Should().Be(150_000);
        files["support.json"].Should().Contain("Câu hỏi của khách").And.Contain("Câu trả lời của nhân viên");
        files["bank_notifications.json"].Should().Contain("com.VCB");
    }

    /// <summary>Gieo dữ liệu nhạy cảm của A và dữ liệu của B trong nhóm chung; trả các chuỗi không được xuất hiện trong gói của A.</summary>
    private async Task<List<string>> SeedSecretsAsync(LoanTestApi a, LoanTestApi b, Guid aWallet, Guid bShared) => await a.InDbAsync<List<string>>(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        (await db.Users.SingleAsync(u => u.Id == b.UserId)).DisplayName = "Tên Riêng Của Thành Viên B";
        var group = Insert(db, new Group { Id = Guid.NewGuid(), Name = "Phòng trọ", CreatedByUserId = b.UserId, CreatedAt = now, UpdatedAt = now });
        Insert(db, new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = b.UserId, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
        Insert(db, new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = a.UserId, Role = GroupRole.Member, JoinedAt = now, UpdatedAt = now });
        var shared = await db.Transactions.SingleAsync(t => t.Id == bShared);
        shared.GroupId = group.Id;
        shared.SharedAt = now;
        shared.Description = "Ghi chú riêng của B";
        var bill = Insert(db, new GroupBill { Id = Guid.NewGuid(), GroupId = group.Id, Title = "Tiền điện tháng 11 B tự đặt tên", AmountCents = 300_000,
            PaidByUserId = b.UserId, CreatedByUserId = b.UserId, OccurredAt = now, Note = "B ghi chú hoá đơn", CreatedAt = now });
        Insert(db, new GroupBillSplit { Id = Guid.NewGuid(), BillId = bill.Id, UserId = a.UserId, ShareCents = 150_000 });
        Insert(db, new GroupBillSplit { Id = Guid.NewGuid(), BillId = bill.Id, UserId = b.UserId, ShareCents = 150_000 });
        Insert(db, new DeviceToken { Id = Guid.NewGuid(), UserId = a.UserId, Token = "fcm-secret-token-of-a", Platform = DevicePlatform.Android, CreatedAt = now, LastSeenAt = now });
        Insert(db, new NotificationLog { Id = Guid.NewGuid(), UserId = a.UserId, FinancialAccountId = aWallet, PackageName = "com.VCB",
            NotificationTitle = "Tiêu đề thô VCB", NotificationBody = "TK 0011 -99.000VND NOI DUNG THO BI MAT", ContentHash = "hash-thong-bao-bi-mat-1234",
            ReceivedAt = now, Status = NotificationLogStatus.Processed, CreatedAt = now });
        var ticket = Insert(db, new SupportTicket { Id = Guid.NewGuid(), RequesterUserId = a.UserId, Subject = "Hỏi về ví", Category = SupportTicketCategory.Question,
            Priority = SupportTicketPriority.P3, Status = SupportTicketStatus.InProgress, FirstResponseDueAt = now, ResolveDueAt = now, CreatedAt = now, UpdatedAt = now });
        Insert(db, new SupportTicketMessage { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorUserId = a.UserId, Kind = SupportMessageKind.Customer, Body = "Câu hỏi của khách", CreatedAt = now });
        Insert(db, new SupportTicketMessage { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorUserId = b.UserId, Kind = SupportMessageKind.Staff, Body = "Câu trả lời của nhân viên", CreatedAt = now });
        Insert(db, new SupportTicketMessage { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorUserId = b.UserId, Kind = SupportMessageKind.InternalNote, Body = "Ghi chú nội bộ bí mật", CreatedAt = now });
        await db.SaveChangesAsync();

        var userA = await db.Users.SingleAsync(u => u.Id == a.UserId);
        var userB = await db.Users.SingleAsync(u => u.Id == b.UserId);
        var refreshHashes = await db.RefreshTokens.Where(t => t.UserId == a.UserId).Select(t => t.TokenHash).ToListAsync();
        refreshHashes.Should().NotBeEmpty();
        return
        [
            userA.PasswordHash!, .. refreshHashes, "fcm-secret-token-of-a", "NOI DUNG THO BI MAT", "Tiêu đề thô VCB", "hash-thong-bao-bi-mat-1234",
            "Ghi chú nội bộ bí mật", userB.Email, userB.DisplayName, b.UserId.ToString(), bShared.ToString(), "Nhà hàng riêng của B", "Ghi chú riêng của B",
            "777000", "Ví B", "Tiền điện tháng 11 B tự đặt tên", "B ghi chú hoá đơn",
        ];
    });

    [Fact]
    public async Task WhenTheAssistantServiceIsDown_ThePackageIsStillBuilt_AndSaysSo()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var agent = new Mock<IAgentAiClient>();
        agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        using var scope = _factory.Services.CreateScope();
        var builder = new PersonalDataArchiveBuilder(scope.ServiceProvider.GetRequiredService<IPersonalDataReader>(),
            scope.ServiceProvider.GetRequiredService<IDataExportRepository>(), agent.Object, NullLogger<PersonalDataArchiveBuilder>.Instance)
        { AgentRetryDelay = TimeSpan.Zero };

        var files = await BuildAsync(api.UserId, builder);

        JsonDocument.Parse(files["agent_memory.json"]).RootElement.GetProperty("data").GetProperty("unavailable").GetBoolean().Should().BeTrue();
        files["README.txt"].Should().Contain("Trợ lý không trả lời");
        agent.Verify(a => a.GetMemoryAsync(api.UserId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task OverTheSizeLimit_TheBuildStopsWithArchiveTooLarge()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        using var scope = _factory.Services.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<PersonalDataArchiveBuilder>();

        await FluentActions.Invoking(() => builder.BuildAsync(api.UserId, new MemoryStream(), 1_000, DateTimeOffset.UtcNow))
            .Should().ThrowAsync<ArchiveTooLargeException>();
    }

    [Fact]
    public async Task DeletedTransactions_GoToTheirOwnFile_WithTheDeletionTime()
    {
        var api = await new LoanTestApi(_factory, _client).SignUpAsync();
        var wallet = await api.WalletAsync();
        var kept = await api.TransactionAsync(wallet, null, 10_000, merchant: "Giữ lại");
        var gone = await api.TransactionAsync(wallet, null, 20_000, merchant: "Đã xoá");
        await api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{gone}", null, System.Net.HttpStatusCode.NoContent);

        var files = await BuildAsync(api.UserId);

        var live = CsvTestReader.Read(Encoding.UTF8.GetBytes(files["transactions.csv"]));
        var deleted = CsvTestReader.Read(Encoding.UTF8.GetBytes(files["transactions_deleted.csv"]));
        live.Select(r => r["Mã giao dịch"]).Should().Equal(kept.ToString());
        deleted.Single()["Mã giao dịch"].Should().Be(gone.ToString());
        deleted.Single()["Đã xoá lúc"].Should().EndWith("+07:00");
    }
}
