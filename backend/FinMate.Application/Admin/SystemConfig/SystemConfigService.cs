using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Admin.SystemConfig;

/// <summary>Cấu hình hệ thống đang chạy hoặc một phiên bản cũ, kèm ai lưu và lúc nào (version 0 = chưa ai lưu, đang dùng mặc định).</summary>
public sealed record SystemConfigView(int Version, SystemConfigDocument Content, string? Note, Guid? UpdatedBy, DateTimeOffset? UpdatedAt);

public sealed record SystemConfigVersionSummary(int Version, string Status, string? Note, Guid CreatedBy, DateTimeOffset CreatedAt);

public sealed record SystemConfigVersionList(IReadOnlyList<SystemConfigVersionSummary> Items, int Total);

public interface ISystemConfigService
{
    Task<SystemConfigView> GetActiveAsync(CancellationToken ct = default);

    Task<SystemConfigView> GetVersionAsync(int version, CancellationToken ct = default);

    Task<SystemConfigVersionList> ListVersionsAsync(int limit, int offset, CancellationToken ct = default);

    /// <summary>Lưu nội dung mới thành phiên bản kế tiếp. <paramref name="expectedVersion"/> là bản đang chạy mà người sửa nhìn thấy; khác thực tế thì bị từ chối (409).</summary>
    Task<SystemConfigView> SaveAsync(Guid adminId, string? ip, int expectedVersion, SystemConfigDocument content, string note, CancellationToken ct = default);

    /// <summary>Hoàn tác = tạo phiên bản MỚI có nội dung của <paramref name="targetVersion"/>: lịch sử vẫn là một dãy tuyến tính, không dòng nào bị ghi đè.</summary>
    Task<SystemConfigView> RollbackAsync(Guid adminId, string? ip, int targetVersion, int expectedVersion, string note, CancellationToken ct = default);
}

public class SystemConfigService : ISystemConfigService
{
    private const int MinNoteLength = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISystemConfigRepository _repository;
    private readonly ISystemConfigProvider _provider;
    private readonly IValidator<SystemConfigDocument> _validator;
    private readonly IAuditLogService _auditLogService;

    public SystemConfigService(
        ISystemConfigRepository repository,
        ISystemConfigProvider provider,
        IValidator<SystemConfigDocument> validator,
        IAuditLogService auditLogService)
    {
        _repository = repository;
        _provider = provider;
        _validator = validator;
        _auditLogService = auditLogService;
    }

    public async Task<SystemConfigView> GetActiveAsync(CancellationToken ct = default)
    {
        var row = await _repository.GetActiveAsync(SystemConfigNames.App, ct);
        return row is null ? new SystemConfigView(0, new SystemConfigDocument(), null, null, null) : ToView(row);
    }

    public async Task<SystemConfigView> GetVersionAsync(int version, CancellationToken ct = default)
    {
        var row = await _repository.GetVersionAsync(SystemConfigNames.App, version, ct)
            ?? throw new NotFoundException("SystemConfigVersion", version.ToString());
        return ToView(row);
    }

    public async Task<SystemConfigVersionList> ListVersionsAsync(int limit, int offset, CancellationToken ct = default)
    {
        var (items, total) = await _repository.ListAsync(SystemConfigNames.App, limit, offset, ct);
        return new SystemConfigVersionList(
            items.Select(row => new SystemConfigVersionSummary(row.Version, row.Status.ToString().ToLowerInvariant(), row.Note, row.CreatedBy, row.CreatedAt)).ToList(),
            total);
    }

    public async Task<SystemConfigView> SaveAsync(
        Guid adminId, string? ip, int expectedVersion, SystemConfigDocument content, string note, CancellationToken ct = default)
    {
        RequireNote(note);
        await _validator.ValidateAndThrowAsync(content, ct);
        return await CommitAsync(adminId, ip, "save", null, expectedVersion, content, note, ct);
    }

    public async Task<SystemConfigView> RollbackAsync(
        Guid adminId, string? ip, int targetVersion, int expectedVersion, string note, CancellationToken ct = default)
    {
        RequireNote(note);
        var target = await _repository.GetVersionAsync(SystemConfigNames.App, targetVersion, ct)
            ?? throw new NotFoundException("SystemConfigVersion", targetVersion.ToString());
        var content = Deserialize(target.Content);
        await _validator.ValidateAndThrowAsync(content, ct);  // luật kiểm tra có thể đã chặt hơn từ lúc phiên bản cũ được lưu
        return await CommitAsync(adminId, ip, "rollback", targetVersion, expectedVersion, content, note, ct);
    }

    private async Task<SystemConfigView> CommitAsync(
        Guid adminId, string? ip, string action, int? fromVersion, int expectedVersion, SystemConfigDocument content, string note, CancellationToken ct)
    {
        var before = (await _repository.GetActiveAsync(SystemConfigNames.App, ct)) is { } active ? Deserialize(active.Content) : new SystemConfigDocument();
        var next = new SystemConfigVersion
        {
            Id = Guid.NewGuid(),
            Name = SystemConfigNames.App,
            Status = SystemConfigStatus.Active,
            Content = JsonSerializer.Serialize(content, Json),
            Note = note.Trim(),
            CreatedBy = adminId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (!await _repository.TryActivateNextAsync(next, expectedVersion, ct))
        {
            throw new ConflictException(AdminErrorCodes.SystemConfigStale, "Có người vừa đổi cấu hình hệ thống. Tải lại để xem bản mới nhất rồi thử lại.");
        }

        _provider.Invalidate();

        // Chỉ ghi tên các phần đã đổi, không ghi nội dung: giá trị đã nằm trong chính phiên bản vừa lưu.
        await _auditLogService.LogAsync(
            AuditEvents.AdminSystemConfigChanged,
            adminId,
            ip,
            new { Action = action, Version = next.Version, FromVersion = fromVersion, Note = next.Note, ChangedSections = ChangedSections(before, content) },
            ct);

        return ToView(next);
    }

    private static void RequireNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length < MinNoteLength)
        {
            throw new BusinessRuleException(AdminErrorCodes.SystemConfigNoteRequired, $"Cần ghi lý do đổi (ít nhất {MinNoteLength} ký tự).");
        }
    }

    private static SystemConfigView ToView(SystemConfigVersion row) => new(row.Version, Deserialize(row.Content), row.Note, row.CreatedBy, row.CreatedAt);

    /// <summary>Đọc lại nội dung đã lưu; trường mà code thêm SAU khi phiên bản được lưu nhận giá trị mặc định.</summary>
    private static SystemConfigDocument Deserialize(string json) => JsonSerializer.Deserialize<SystemConfigDocument>(json, Json) ?? new SystemConfigDocument();

    private static List<string> ChangedSections(SystemConfigDocument before, SystemConfigDocument after)
    {
        var sections = new List<string>();
        void Compare<T>(string name, T a, T b)
        {
            if (JsonSerializer.Serialize(a, Json) != JsonSerializer.Serialize(b, Json))
            {
                sections.Add(name);
            }
        }

        Compare("maintenance", before.Maintenance, after.Maintenance);
        Compare("appVersion", before.AppVersion, after.AppVersion);
        Compare("features", before.Features, after.Features);
        Compare("limits", before.Limits, after.Limits);
        Compare("sync", before.Sync, after.Sync);
        Compare("theme", before.Theme, after.Theme);
        Compare("qrTransfer", before.QrTransfer, after.QrTransfer);
        Compare("financeMethods", before.FinanceMethods, after.FinanceMethods);
        return sections;
    }
}
