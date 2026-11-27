using FinMate.Application.Admin;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Ops;

public sealed record OpsNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>Tên loại thông báo dạng snake_case, đúng như cột <c>type</c> và như <c>DisabledTypes</c> trong cấu hình.</summary>
public static class OpsNotificationTypes
{
    public static string Name(OpsNotificationType type)
    {
        var name = type.ToString();
        return string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    public static bool IsKnown(string? name)
        => name is not null && Enum.GetValues<OpsNotificationType>().Any(t => Name(t) == name);
}

/// <summary>
/// Chỗ DUY NHẤT tạo thông báo cho đội vận hành. Người nhận chọn theo quyền (<see cref="OpsCapability"/>) đang có trong DB chứ không theo tên vai trò,
/// nên Admin chỉnh quyền của Staff/OpsManager thì thông báo đi theo. Gọi từ luồng nghiệp vụ thì không bao giờ ném lỗi: không có thông báo là chuyện nhỏ,
/// làm hỏng việc đã lưu là chuyện lớn.
/// </summary>
public interface IOpsNotificationService
{
    /// <summary>Gửi cho mọi tài khoản nội bộ đang hoạt động có quyền <paramref name="capability"/>, trừ những người trong <paramref name="exclude"/> (người gây ra việc, chủ ca).</summary>
    Task NotifyCapabilityAsync(
        OpsNotificationType type,
        OpsCapability capability,
        string title,
        string body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        IReadOnlyCollection<Guid>? exclude = null,
        CancellationToken ct = default);

    /// <summary>Gửi cho đúng một người, chỉ khi người đó vẫn là tài khoản nội bộ đang hoạt động.</summary>
    Task NotifyUserAsync(
        OpsNotificationType type,
        Guid userId,
        string title,
        string body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct = default);

    Task<PagedResult<OpsNotificationDto>> ListAsync(Guid actorId, bool unreadOnly, string? search, int offset, int limit, CancellationToken ct = default);
    Task<int> CountUnreadAsync(Guid actorId, CancellationToken ct = default);
    Task MarkReadAsync(Guid actorId, Guid id, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(Guid actorId, CancellationToken ct = default);

    /// <summary>Xóa thông báo quá <c>RetentionDays</c>. Trả số dòng đã xóa.</summary>
    Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class OpsNotificationService : IOpsNotificationService
{
    private static readonly UserRole[] InternalRoles = [UserRole.Staff, UserRole.OpsManager, UserRole.Admin];

    private readonly IOpsNotificationRepository _repository;
    private readonly IOpsAccessService _access;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<OpsNotificationService> _logger;

    public OpsNotificationService(
        IOpsNotificationRepository repository, IOpsAccessService access, ISystemConfigProvider config, ILogger<OpsNotificationService> logger)
    {
        _repository = repository;
        _access = access;
        _config = config;
        _logger = logger;
    }

    public async Task NotifyCapabilityAsync(
        OpsNotificationType type, OpsCapability capability, string title, string body, string? relatedEntityType, Guid? relatedEntityId,
        IReadOnlyCollection<Guid>? exclude = null, CancellationToken ct = default)
    {
        try
        {
            if (!await IsEnabledAsync(type, ct))
            {
                return;
            }

            var roles = new List<UserRole>();
            foreach (var role in InternalRoles)
            {
                if (await _access.HasAsync(role, capability, ct))
                {
                    roles.Add(role);
                }
            }

            var recipients = roles.Count == 0 ? [] : await _repository.ListActiveUserIdsByRolesAsync(roles, ct);
            await AddAsync(type, recipients.Where(id => exclude is null || !exclude.Contains(id)).ToList(), title, body, relatedEntityType, relatedEntityId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Chỉ tên kiểu lỗi: thông báo không mang dữ liệu khách nhưng lỗi thì có thể.
            _logger.LogWarning("Could not create ops notification {Type} ({ErrorType})", OpsNotificationTypes.Name(type), ex.GetType().Name);
        }
    }

    public async Task NotifyUserAsync(
        OpsNotificationType type, Guid userId, string title, string body, string? relatedEntityType, Guid? relatedEntityId, CancellationToken ct = default)
    {
        try
        {
            if (!await IsEnabledAsync(type, ct))
            {
                return;
            }

            var active = await _repository.ListActiveUserIdsByRolesAsync(InternalRoles, ct);
            if (active.Contains(userId))
            {
                await AddAsync(type, [userId], title, body, relatedEntityType, relatedEntityId, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not create ops notification {Type} ({ErrorType})", OpsNotificationTypes.Name(type), ex.GetType().Name);
        }
    }

    public async Task<PagedResult<OpsNotificationDto>> ListAsync(Guid actorId, bool unreadOnly, string? search, int offset, int limit, CancellationToken ct = default)
    {
        await _access.RequireInternalAsync(actorId, ct);
        var (skip, take) = AdminPaging.Normalize(offset, limit);
        var (items, total) = await _repository.ListAsync(actorId, unreadOnly, search, skip, take, ct);
        return new PagedResult<OpsNotificationDto>(items.Select(ToDto).ToList(), total, skip, take);
    }

    public async Task<int> CountUnreadAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireInternalAsync(actorId, ct);
        return await _repository.CountUnreadAsync(actorId, ct);
    }

    public async Task MarkReadAsync(Guid actorId, Guid id, CancellationToken ct = default)
    {
        await _access.RequireInternalAsync(actorId, ct);
        if (!await _repository.MarkReadAsync(actorId, id, DateTimeOffset.UtcNow, ct))
        {
            throw new NotFoundException("OpsNotification", id);
        }
    }

    public async Task<int> MarkAllReadAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireInternalAsync(actorId, ct);
        return await _repository.MarkAllReadAsync(actorId, DateTimeOffset.UtcNow, ct);
    }

    public async Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.OpsNotifications;
        return await _repository.DeleteOlderThanAsync(now.AddDays(-settings.RetentionDays), ct);
    }

    private async Task<bool> IsEnabledAsync(OpsNotificationType type, CancellationToken ct)
    {
        var settings = (await _config.GetAsync(ct)).Document.OpsNotifications;
        return settings.Enabled && !settings.DisabledTypes.Contains(OpsNotificationTypes.Name(type));
    }

    private async Task AddAsync(
        OpsNotificationType type, IReadOnlyList<Guid> recipients, string title, string body, string? relatedEntityType, Guid? relatedEntityId, CancellationToken ct)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        // Cùng việc mà người nhận chưa đọc thông báo trước thì không thêm: nhắc lại sự cố hay báo cáo dồn về một nội dung không được thành chục dòng.
        var alreadyHave = relatedEntityId is null
            ? new HashSet<Guid>()
            : await _repository.ListUsersWithUnreadAsync(type, relatedEntityId, recipients.ToList(), ct);

        var now = DateTimeOffset.UtcNow;
        var rows = recipients.Where(id => !alreadyHave.Contains(id)).Distinct().Select(id => new OpsNotification
        {
            Id = Guid.NewGuid(),
            UserId = id,
            Type = type,
            Title = title,
            Body = body,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = now,
        }).ToList();
        if (rows.Count > 0)
        {
            await _repository.AddRangeAsync(rows, ct);
        }
    }

    private static OpsNotificationDto ToDto(OpsNotification n)
        => new(n.Id, OpsNotificationTypes.Name(n.Type), n.Title, n.Body, n.RelatedEntityType, n.RelatedEntityId, n.CreatedAt, n.ReadAt);
}
