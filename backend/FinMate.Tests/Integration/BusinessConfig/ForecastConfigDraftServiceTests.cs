using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.ForecastConfig;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FinMate.Tests.Integration.BusinessConfig;

/// <summary>Phase 95 §6.3: mọi chuyển trạng thái của bản nháp tham số dự báo trên Postgres thật, với repository và validator thật; quyền giả theo vai trò.</summary>
public class ForecastConfigDraftServiceTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public ForecastConfigDraftServiceTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Quyền theo vai trò mặc định; Admin đủ mọi quyền. Không đọc DB, đủ cho service.</summary>
    private sealed class RoleAccess(Dictionary<Guid, UserRole> roles) : IOpsAccessService
    {
        public Task<User> RequireInternalAsync(Guid actorId, CancellationToken ct = default)
            => roles.TryGetValue(actorId, out var role) && role != UserRole.User
                ? Task.FromResult(new User { Id = actorId, Role = role })
                : throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");

        public async Task<User> RequireAsync(Guid actorId, OpsCapability capability, CancellationToken ct = default)
        {
            var user = await RequireInternalAsync(actorId, ct);
            return await HasAsync(user.Role, capability, ct) ? user : throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
        }

        public Task<bool> HasAsync(UserRole role, OpsCapability capability, CancellationToken ct = default)
            => Task.FromResult((role == UserRole.Admin ? RolePermissionRules.All : OpsPermissions.For(role)).HasFlag(capability));

        public Task<OpsCapability> CapabilitiesAsync(UserRole role, CancellationToken ct = default)
            => Task.FromResult(role == UserRole.Admin ? RolePermissionRules.All : OpsPermissions.For(role));
    }

    private sealed record Ctx(ForecastConfigDraftService Service, FinMateDbContext Db, SystemConfigRepository Repo, Mock<ISystemConfigProvider> Provider, Mock<IAuditLogService> Audit,
        Guid Staff, Guid Staff2, Guid Manager, Guid Manager2, Guid Admin) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<Ctx> ArrangeAsync()
    {
        var db = _fixture.NewContext(_fixture.ConnectionStringFor($"fcs_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        var repo = new SystemConfigRepository(db);
        Guid staff = Guid.NewGuid(), staff2 = Guid.NewGuid(), manager = Guid.NewGuid(), manager2 = Guid.NewGuid(), admin = Guid.NewGuid();
        var access = new RoleAccess(new()
        {
            [staff] = UserRole.Staff, [staff2] = UserRole.Staff, [manager] = UserRole.OpsManager, [manager2] = UserRole.OpsManager, [admin] = UserRole.Admin,
        });
        var provider = new Mock<ISystemConfigProvider>();
        var audit = new Mock<IAuditLogService>();
        var service = new ForecastConfigDraftService(repo, provider.Object, new SystemConfigDocumentValidator(), access, audit.Object);
        return new Ctx(service, db, repo, provider, audit, staff, staff2, manager, manager2, admin);
    }

    private static JsonElement Changes(string json) => JsonDocument.Parse(json).RootElement;

    private static CreateForecastConfigDraft Create(string changes, Guid? from = null, int? version = null)
        => new(Changes(changes), from, version, "đổi tham số dự báo");

    private static async Task<int> AdminSaveAsync(Ctx c, Func<SystemConfigDocument, SystemConfigDocument> edit)
    {
        var active = await c.Repo.GetActiveAsync(SystemConfigNames.App);
        var doc = active is null ? new SystemConfigDocument() : JsonSerializer.Deserialize<SystemConfigDocument>(active.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var next = new SystemConfigVersion
        {
            Id = Guid.NewGuid(), Name = SystemConfigNames.App, Status = SystemConfigStatus.Active, CreatedBy = c.Admin, CreatedAt = DateTimeOffset.UtcNow, Note = "admin",
            Content = JsonSerializer.Serialize(edit(doc), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        (await c.Repo.TryActivateNextAsync(next, active?.Version ?? 0)).Should().BeTrue();
        c.Db.ChangeTracker.Clear();
        return next.Version!.Value;
    }

    private static async Task<ForecastConfigSections> ActiveSectionsAsync(Ctx c)
        => (await c.Service.GetCurrentAsync(c.Admin)).Sections;

    [Fact]
    public async Task DraftSubmitApprove_ChangesTheActiveConfigOnlyOnApproval()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);

        draft.Status.Should().Be("draft");
        draft.Diff.Should().ContainSingle(d => d.Path == "forecast.lowBalanceHorizonDays");
        (await ActiveSectionsAsync(c)).Forecast.LowBalanceHorizonDays.Should().Be(14, "bản nháp không ảnh hưởng runtime");

        (await c.Service.SubmitAsync(c.Staff, draft.Id, null)).Status.Should().Be("pending_approval");
        (await ActiveSectionsAsync(c)).Forecast.LowBalanceHorizonDays.Should().Be(14, "bản chờ duyệt cũng không");

        var approved = await c.Service.ApproveAsync(c.Manager, draft.Id, "ok", null);

        approved.Status.Should().Be("active");
        approved.ApprovedVersion.Should().Be(1);
        approved.DecidedBy.Should().Be(c.Manager);
        approved.CreatedBy.Should().Be(c.Staff);
        (await ActiveSectionsAsync(c)).Forecast.LowBalanceHorizonDays.Should().Be(21);
        c.Provider.Verify(p => p.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task NoOneDecidesTheirOwnDraft_ButAnAdminDecidesSomeoneElses()
    {
        await using var c = await ArrangeAsync();
        var own = await c.Service.CreateAsync(c.Manager, Create("""{"limits":{"alertT1Percent":75}}"""), null);
        await c.Service.SubmitAsync(c.Manager, own.Id, null);

        var approve = () => c.Service.ApproveAsync(c.Manager, own.Id, null, null);
        var reject = () => c.Service.RejectAsync(c.Manager, own.Id, "không ổn", null);
        (await approve.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.OwnDraft);
        await reject.Should().ThrowAsync<ForbiddenException>();

        (await c.Service.ApproveAsync(c.Admin, own.Id, null, null)).Status.Should().Be("active");
    }

    [Fact]
    public async Task StaffCannotApprove()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"incomeLateDays":5}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);

        var act = () => c.Service.ApproveAsync(c.Staff2, draft.Id, null, null);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ApprovingAStaleDraftIsRefused_WithoutMerging_AndTheDraftKeepsWaiting()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);
        await AdminSaveAsync(c, d => d with { Maintenance = new MaintenanceSettings { Enabled = true } });

        var act = () => c.Service.ApproveAsync(c.Manager, draft.Id, null, null);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.Stale);
        c.Db.ChangeTracker.Clear();
        var reloaded = await c.Service.GetAsync(c.Manager, draft.Id);
        reloaded.Status.Should().Be("pending_approval");
        reloaded.Stale.Should().BeTrue();
        (await ActiveSectionsAsync(c)).Forecast.LowBalanceHorizonDays.Should().Be(14);
    }

    [Fact]
    public async Task S1_RedoingMyStalePendingDraft_CancelsItAndFreesThePendingSlot()
    {
        await using var c = await ArrangeAsync();
        var first = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, first.Id, null);
        await AdminSaveAsync(c, d => d with { Maintenance = new MaintenanceSettings { Enabled = true } });

        var redo = await c.Service.CreateAsync(c.Staff, new CreateForecastConfigDraft(null, first.Id, null, "làm lại"), null);
        await c.Service.SubmitAsync(c.Staff, redo.Id, null);
        var approved = await c.Service.ApproveAsync(c.Manager, redo.Id, null, null);

        c.Db.ChangeTracker.Clear();
        (await c.Service.GetAsync(c.Staff, first.Id)).Status.Should().Be("cancelled");
        approved.ApprovedVersion.Should().Be(2);
        var current = await c.Service.GetCurrentAsync(c.Admin);
        current.Sections.Forecast.LowBalanceHorizonDays.Should().Be(21);
        var maintenance = JsonSerializer.Deserialize<SystemConfigDocument>((await c.Repo.GetActiveAsync(SystemConfigNames.App))!.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Maintenance;
        maintenance.Enabled.Should().BeTrue("thay đổi Admin vừa lưu ở nhóm khác vẫn còn");
    }

    [Fact]
    public async Task S1_SomeoneElsesPendingDraftCannotBeRedone_AndAFreshOneMustBeWithdrawnFirst()
    {
        await using var c = await ArrangeAsync();
        var first = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, first.Id, null);

        var fresh = () => c.Service.CreateAsync(c.Staff, new CreateForecastConfigDraft(null, first.Id, null, "làm lại"), null);
        (await fresh.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.InvalidSource);

        await AdminSaveAsync(c, d => d with { Maintenance = new MaintenanceSettings { Enabled = true } });
        var other = () => c.Service.CreateAsync(c.Staff2, new CreateForecastConfigDraft(null, first.Id, null, "làm lại hộ"), null);
        await other.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AReviewerMayRejectAStaleDraft()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);
        await AdminSaveAsync(c, d => d with { Maintenance = new MaintenanceSettings { Enabled = true } });

        var rejected = await c.Service.RejectAsync(c.Manager, draft.Id, "đã cũ, soạn lại", null);

        rejected.Status.Should().Be("rejected");
        rejected.DecisionNote.Should().Be("đã cũ, soạn lại");
    }

    [Fact]
    public async Task OnlyOnePendingAtATime()
    {
        await using var c = await ArrangeAsync();
        var a = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        var b = await c.Service.CreateAsync(c.Staff2, Create("""{"forecast":{"incomeLateDays":5}}"""), null);
        await c.Service.SubmitAsync(c.Staff, a.Id, null);

        var act = () => c.Service.SubmitAsync(c.Staff2, b.Id, null);

        var error = (await act.Should().ThrowAsync<ConflictException>()).Which;
        error.ErrorCode.Should().Be(ForecastConfigErrorCodes.PendingExists);
        error.Message.Should().Contain(a.Id.ToString());
    }

    [Fact]
    public async Task RejectRequiresANote_ThenRedoCarriesTheChangesAndAddsNewOnes()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);
        var noNote = () => c.Service.RejectAsync(c.Manager, draft.Id, " ", null);
        (await noNote.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.NoteRequired);
        await c.Service.RejectAsync(c.Manager, draft.Id, "thêm ngưỡng T1", null);

        var redo = await c.Service.CreateAsync(c.Staff, new CreateForecastConfigDraft(Changes("""{"limits":{"alertT1Percent":75}}"""), draft.Id, null, "sửa theo góp ý"), null);

        redo.Sections.Forecast.LowBalanceHorizonDays.Should().Be(21);
        redo.Sections.Limits.AlertT1Percent.Should().Be(75);
        redo.Diff.Select(d => d.Path).Should().BeEquivalentTo("forecast.lowBalanceHorizonDays", "limits.alertT1Percent");
    }

    [Fact]
    public async Task S2_RedoReportsFieldsAnAdminAlsoChanged()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);
        await AdminSaveAsync(c, d => d with { Forecast = d.Forecast with { LowBalanceHorizonDays = 30 } });

        var redo = await c.Service.CreateAsync(c.Staff, new CreateForecastConfigDraft(null, draft.Id, null, "làm lại"), null);

        var conflict = redo.Conflicts.Should().ContainSingle().Subject;
        conflict.Path.Should().Be("forecast.lowBalanceHorizonDays");
        conflict.ActiveValue!.GetValue<int>().Should().Be(30);
        conflict.DraftValue!.GetValue<int>().Should().Be(21);
        c.Db.ChangeTracker.Clear();
        (await c.Service.GetAsync(c.Manager, redo.Id)).Conflicts.Should().ContainSingle("xung đột được lưu và trả lại khi xem");
    }

    [Fact]
    public async Task UpdateNeedsTheLastSeenTimestamp_AndOnlyTheAuthorEdits()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);

        var stale = () => c.Service.UpdateAsync(c.Staff, draft.Id, new UpdateForecastConfigDraft(Changes("""{"forecast":{"incomeLateDays":5}}"""), null, draft.UpdatedAt!.Value.AddSeconds(-1)), null);
        (await stale.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.DraftChanged);
        var other = () => c.Service.UpdateAsync(c.Staff2, draft.Id, new UpdateForecastConfigDraft(Changes("{}"), null, draft.UpdatedAt), null);
        (await other.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.NotAuthor);

        var updated = await c.Service.UpdateAsync(c.Staff, draft.Id, new UpdateForecastConfigDraft(Changes("""{"forecast":{"incomeLateDays":5}}"""), null, draft.UpdatedAt), null);

        updated.Sections.Forecast.IncomeLateDays.Should().Be(5);
        updated.Sections.Forecast.LowBalanceHorizonDays.Should().Be(21);
    }

    [Fact]
    public async Task InvalidValuesAreRefusedWithCrossFieldRules()
    {
        await using var c = await ArrangeAsync();

        var act = () => c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"minHistoryDays":200,"historyWindowDays":90}}"""), null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task WithdrawAndCancel_AreForTheAuthor()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);

        (await c.Service.WithdrawAsync(c.Staff, draft.Id, null)).Status.Should().Be("draft");
        (await c.Service.CancelAsync(c.Staff, draft.Id, null)).Status.Should().Be("cancelled");
        var again = () => c.Service.SubmitAsync(c.Staff, draft.Id, null);
        (await again.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ForecastConfigErrorCodes.WrongStatus);
    }

    [Fact]
    public async Task FromVersion_RestoresAnOldVersionsValuesAsANewDraft()
    {
        await using var c = await ArrangeAsync();
        var v1 = await AdminSaveAsync(c, d => d with { Forecast = d.Forecast with { LowBalanceHorizonDays = 10 } });
        await AdminSaveAsync(c, d => d with { Forecast = d.Forecast with { LowBalanceHorizonDays = 40 } });

        var undo = await c.Service.CreateAsync(c.Staff, new CreateForecastConfigDraft(null, null, v1, "hoàn tác"), null);

        undo.Sections.Forecast.LowBalanceHorizonDays.Should().Be(10);
        undo.Diff.Should().ContainSingle(d => d.Path == "forecast.lowBalanceHorizonDays");
    }

    [Fact]
    public async Task AuditCarriesFieldNamesButNoValuesOrNotes()
    {
        await using var c = await ArrangeAsync();
        var draft = await c.Service.CreateAsync(c.Staff, Create("""{"forecast":{"lowBalanceHorizonDays":21}}"""), null);
        await c.Service.SubmitAsync(c.Staff, draft.Id, null);
        await c.Service.RejectAsync(c.Manager, draft.Id, "ghi chú bí mật", null);

        foreach (var invocation in c.Audit.Invocations)
        {
            var payload = JsonSerializer.Serialize(invocation.Arguments[3]);
            payload.Should().NotContain("ghi chú bí mật").And.NotContain("\"21\"").And.NotContain("đổi tham số dự báo");
        }

        c.Audit.Verify(a => a.LogAsync("Ops.ForecastConfig.DraftCreated", c.Staff, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        c.Audit.Verify(a => a.LogAsync("Ops.ForecastConfig.Rejected", c.Manager, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
