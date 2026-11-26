using System.Text.Json;
using FinMate.Application.Admin;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.ForecastConfig;

public interface IForecastConfigDraftService
{
    Task<ForecastConfigCurrentDto> GetCurrentAsync(Guid actorId, CancellationToken ct = default);
    Task<PagedResult<ForecastConfigDraftDto>> ListAsync(Guid actorId, SystemConfigStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> GetAsync(Guid actorId, Guid id, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> CreateAsync(Guid actorId, CreateForecastConfigDraft command, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> UpdateAsync(Guid actorId, Guid id, UpdateForecastConfigDraft command, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> SubmitAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> WithdrawAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> CancelAsync(Guid actorId, Guid id, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> ApproveAsync(Guid actorId, Guid id, string? note, string? ip, CancellationToken ct = default);
    Task<ForecastConfigDraftDto> RejectAsync(Guid actorId, Guid id, string? note, string? ip, CancellationToken ct = default);
}

/// <summary>
/// Phase 95 (A): Staff soạn tham số dự báo (<c>forecast</c> + <c>limits</c>), OpsManager duyệt. Bản nháp nằm trong <c>system_config_versions</c> và không ảnh hưởng runtime
/// cho tới khi được duyệt; duyệt = chính dòng nháp thành phiên bản đang chạy, chỉ khi phiên bản gốc vẫn đang chạy (không tự gộp). Quyền đọc từ DB mỗi lần.
/// </summary>
public sealed partial class ForecastConfigDraftService : IForecastConfigDraftService
{
    private const string App = SystemConfigNames.App;
    private const string Scope = ForecastConfigSections.Scope;
    private const int MinTextLength = 3;
    private const int MaxTextLength = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISystemConfigRepository _repository;
    private readonly ISystemConfigProvider _provider;
    private readonly IValidator<SystemConfigDocument> _validator;
    private readonly IOpsAccessService _access;
    private readonly IAuditLogService _audit;

    public ForecastConfigDraftService(
        ISystemConfigRepository repository,
        ISystemConfigProvider provider,
        IValidator<SystemConfigDocument> validator,
        IOpsAccessService access,
        IAuditLogService audit)
    {
        _repository = repository;
        _provider = provider;
        _validator = validator;
        _access = access;
        _audit = audit;
    }

    public async Task<ForecastConfigCurrentDto> GetCurrentAsync(Guid actorId, CancellationToken ct = default)
    {
        await RequireReaderAsync(actorId, ct);
        var (version, document) = await ActiveAsync(ct);
        var pending = await _repository.GetPendingAsync(App, Scope, ct);
        return new ForecastConfigCurrentDto(version, ForecastConfigSections.Of(document), pending?.Id);
    }

    public async Task<PagedResult<ForecastConfigDraftDto>> ListAsync(Guid actorId, SystemConfigStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        await RequireReaderAsync(actorId, ct);
        var (skip, take) = AdminPaging.Normalize(offset, limit);
        var (items, total) = await _repository.ListDraftsAsync(App, Scope, status, skip, take, ct);
        var active = await ActiveAsync(ct);
        var dtos = new List<ForecastConfigDraftDto>(items.Count);
        foreach (var item in items)
        {
            dtos.Add(await ToDtoAsync(item, active, ct));
        }

        return new PagedResult<ForecastConfigDraftDto>(dtos, total, skip, take);
    }

    public async Task<ForecastConfigDraftDto> GetAsync(Guid actorId, Guid id, CancellationToken ct = default)
    {
        await RequireReaderAsync(actorId, ct);
        return await ToDtoAsync(await LoadAsync(id, ct), await ActiveAsync(ct), ct);
    }

    // ---- dùng chung

    private async Task RequireReaderAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await _access.RequireInternalAsync(actorId, ct);
        if (!await _access.HasAsync(actor.Role, OpsCapability.DraftForecastConfig, ct)
            && !await _access.HasAsync(actor.Role, OpsCapability.ApproveForecastConfig, ct))
        {
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
        }
    }

    private async Task<SystemConfigVersion> LoadAsync(Guid id, CancellationToken ct)
        => await _repository.GetDraftAsync(App, id, ct) ?? throw new NotFoundException("ForecastConfigDraft", id);

    private async Task<(int Version, SystemConfigDocument Document)> ActiveAsync(CancellationToken ct)
    {
        var row = await _repository.GetActiveAsync(App, ct);
        return row is null ? (0, new SystemConfigDocument()) : (row.Version ?? 0, DeserializeDocument(row.Content));
    }

    private static SystemConfigDocument DeserializeDocument(string json) => JsonSerializer.Deserialize<SystemConfigDocument>(json, Json) ?? new SystemConfigDocument();

    /// <summary>Hai nhóm của một dòng: bản nháp lưu đúng hai nhóm; bản đã duyệt (active/archived) lưu cả tài liệu.</summary>
    private static ForecastConfigSections SectionsOf(SystemConfigVersion row)
        => row.Status is SystemConfigStatus.Active or SystemConfigStatus.Archived
            ? ForecastConfigSections.Of(DeserializeDocument(row.Content))
            : ForecastConfigSections.Deserialize(row.Content);

    private async Task<SystemConfigDocument> DocumentOfVersionAsync(int version, CancellationToken ct)
    {
        if (version == 0)
        {
            return new SystemConfigDocument();
        }

        var row = await _repository.GetVersionAsync(App, version, ct) ?? throw new NotFoundException("SystemConfigVersion", version.ToString());
        return DeserializeDocument(row.Content);
    }

    private IReadOnlyList<ForecastConfigValidationError> Validate(SystemConfigDocument full)
        => _validator.Validate(full).Errors.Select(e => new ForecastConfigValidationError(e.PropertyName, e.ErrorMessage)).ToList();

    private async Task ValidateOrThrowAsync(SystemConfigDocument full, CancellationToken ct) => await _validator.ValidateAndThrowAsync(full, ct);

    private static string RequireText(string? text, string code, string label)
    {
        var clean = text?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length < MinTextLength || clean.Length > MaxTextLength)
        {
            throw new BusinessRuleException(code, $"Cần {label} từ {MinTextLength} đến {MaxTextLength} ký tự.");
        }

        return clean;
    }

    private static void RequireAuthor(SystemConfigVersion draft, Guid actorId)
    {
        if (draft.CreatedBy != actorId)
        {
            throw new ForbiddenException(ForecastConfigErrorCodes.NotAuthor, "Chỉ người soạn mới làm được việc này với bản nháp.");
        }
    }

    private static void RequireStatus(SystemConfigVersion draft, SystemConfigStatus expected)
    {
        if (draft.Status != expected)
        {
            throw new ConflictException(ForecastConfigErrorCodes.WrongStatus, "Bản nháp không còn ở trạng thái phù hợp cho thao tác này.");
        }
    }

    /// <summary>Microsecond: Postgres lưu tới micro giây, so <c>expectedUpdatedAt</c> với giá trị chưa cắt sẽ luôn lệch (cùng bài học của văn bản pháp lý).</summary>
    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static string StatusName(SystemConfigStatus status) => status switch
    {
        SystemConfigStatus.PendingApproval => "pending_approval",
        _ => status.ToString().ToLowerInvariant(),
    };

    private async Task<ForecastConfigDraftDto> ToDtoAsync(SystemConfigVersion row, (int Version, SystemConfigDocument Document) active, CancellationToken ct)
    {
        var sections = SectionsOf(row);
        var open = row.Status is SystemConfigStatus.Draft or SystemConfigStatus.PendingApproval;
        var baseVersion = row.BaseVersion ?? 0;
        var compareTo = open ? ForecastConfigSections.Of(active.Document) : ForecastConfigSections.Of(await DocumentOfVersionAsync(baseVersion, ct));
        var conflicts = row.Conflicts is null ? [] : JsonSerializer.Deserialize<List<ForecastConfigConflict>>(row.Conflicts, Json) ?? [];
        return new ForecastConfigDraftDto(
            row.Id, StatusName(row.Status), row.Note ?? string.Empty, row.CreatedBy, row.CreatedAt, row.UpdatedAt, baseVersion, row.Version,
            row.SubmittedAt, row.DecidedBy, row.DecidedAt, row.DecisionNote,
            open && baseVersion != active.Version,
            sections,
            ForecastConfigDiff.Compute(compareTo, sections),
            conflicts,
            open ? Validate(sections.ApplyTo(active.Document)) : []);
    }

    private Task AuditAsync(string eventType, Guid actorId, string? ip, SystemConfigVersion draft, IEnumerable<ForecastConfigChange> changes, CancellationToken ct)
        => _audit.LogAsync(
            eventType, actorId, ip,
            new { DraftId = draft.Id, draft.BaseVersion, Version = draft.Version, Changed = changes.Select(c => c.Path).ToList() },
            ct);
}
