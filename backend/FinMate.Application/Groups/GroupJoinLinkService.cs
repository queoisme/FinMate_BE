using System.Buffers.Text;
using System.Security.Cryptography;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups;

/// <param name="Code">Dán vào "Nhập mã" hoặc nằm trong link/QR; vào nhóm với vai trò <paramref name="Role"/>.</param>
/// <param name="Enabled">Tắt thì mã không dùng được nữa; bật lại dùng đúng mã cũ.</param>
public sealed record GroupJoinLinkDto(string Code, string JoinUrl, GroupRole Role, bool Enabled);

/// <summary>
/// Mã/link tham gia CHUNG của nhóm, luôn hiện sẵn: không hết hạn, dùng được nhiều lần, ai có mã đều vào được với vai trò Member
/// (khác lời mời qua email hay link một lần ở <see cref="IGroupInvitationService"/>). Chỉ người có <c>InviteMembers</c> xem và quản lý.
/// </summary>
public interface IGroupJoinLinkService
{
    /// <summary>Lần đầu mở thì tạo mã luôn, nên người dùng không phải bấm "tạo".</summary>
    Task<GroupJoinLinkDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Đổi mã mới (mã cũ hết dùng được) và bật lại nếu đang tắt. Dùng khi mã bị lộ.</summary>
    Task<GroupJoinLinkDto> ResetAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    Task<GroupJoinLinkDto> SetEnabledAsync(Guid groupId, Guid userId, bool enabled, CancellationToken ct = default);
}

public class GroupJoinLinkService : IGroupJoinLinkService
{
    private const int CodeBytes = 16;

    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;

    public GroupJoinLinkService(IGroupRepository groups, IGroupAccessService access, ISystemConfigProvider config, IAuditLogService audit)
    {
        _groups = groups;
        _access = access;
        _config = config;
        _audit = audit;
    }

    public async Task<GroupJoinLinkDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        if (access.Group.JoinCode is null)
        {
            access.Group.JoinCode = NewCode();
            await _groups.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(access.Group, ct);
    }

    public async Task<GroupJoinLinkDto> ResetAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        access.Group.JoinCode = NewCode();
        access.Group.JoinCodeEnabled = true;
        access.Group.UpdatedAt = DateTimeOffset.UtcNow;
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupJoinCodeChanged, userId, metadata: new { GroupId = groupId, Action = "reset" }, ct: ct);
        return await ToDtoAsync(access.Group, ct);
    }

    public async Task<GroupJoinLinkDto> SetEnabledAsync(Guid groupId, Guid userId, bool enabled, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        access.Group.JoinCode ??= NewCode();
        access.Group.JoinCodeEnabled = enabled;
        access.Group.UpdatedAt = DateTimeOffset.UtcNow;
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupJoinCodeChanged, userId, metadata: new { GroupId = groupId, Action = enabled ? "enable" : "disable" }, ct: ct);
        return await ToDtoAsync(access.Group, ct);
    }

    private async Task<GroupJoinLinkDto> ToDtoAsync(Domain.Entities.Group group, CancellationToken ct)
    {
        var baseUrl = (await _config.GetAsync(ct)).Document.Groups.InviteLinkBaseUrl.TrimEnd('/');
        return new GroupJoinLinkDto(group.JoinCode!, $"{baseUrl}/{group.JoinCode}", GroupRole.Member, group.JoinCodeEnabled);
    }

    private static string NewCode() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(CodeBytes));
}
