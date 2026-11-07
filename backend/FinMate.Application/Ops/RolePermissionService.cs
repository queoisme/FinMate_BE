using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Enums;

namespace FinMate.Application.Ops;

public static class RolePermissionErrorCodes
{
    public const string RoleNotEditable = "ROLE_PERMISSIONS_ROLE_NOT_EDITABLE";
    public const string UnknownCapability = "ROLE_PERMISSIONS_UNKNOWN_CAPABILITY";
    public const string AdminOnlyCapability = "ROLE_PERMISSIONS_ADMIN_ONLY";
    public const string BrokenDependency = "ROLE_PERMISSIONS_BROKEN_DEPENDENCY";
}

/// <summary>Quy tắc cố định quanh việc Admin chỉnh quyền: vai trò nào sửa được, quyền nào chỉ Admin có, quyền nào kéo theo quyền nào.</summary>
public static class RolePermissionRules
{
    /// <summary>Admin luôn đủ quyền (không để Admin tự khóa mình) và User không có quyền nội bộ, nên chỉ hai vai trò này sửa được.</summary>
    public static readonly IReadOnlyList<UserRole> EditableRoles = [UserRole.Staff, UserRole.OpsManager];

    /// <summary>Quyền gắn với chi tiết kỹ thuật và trang /admin/*, chỉ Admin có.</summary>
    public const OpsCapability AdminOnly = OpsCapability.ManageSystem;

    public static readonly OpsCapability All = Enum.GetValues<OpsCapability>().Aggregate(OpsCapability.None, (all, c) => all | c);

    /// <summary>Quyền cần có thì quyền kia mới dùng được: xem dữ liệu khách đi qua ca đang phụ trách, và tạo nháp cần xem được dữ liệu khách.</summary>
    private static readonly (OpsCapability Capability, OpsCapability Requires)[] Dependencies =
    [
        (OpsCapability.AccessCustomerData, OpsCapability.HandleTickets),
        (OpsCapability.CreateDraftTransactions, OpsCapability.AccessCustomerData),
    ];

    public static IReadOnlyList<string> CatalogNames => OpsPermissions.Names(All);

    public static IEnumerable<string> BrokenDependencies(OpsCapability granted)
        => Dependencies
            .Where(d => granted.HasFlag(d.Capability) && !granted.HasFlag(d.Requires))
            .Select(d => $"{d.Capability} cần có {d.Requires}.");
}

public sealed record RolePermissionDto(string Role, bool Editable, IReadOnlyList<string> Capabilities);

/// <param name="Catalog">Mọi quyền hệ thống có, để web dựng ma trận mà không tự liệt kê.</param>
/// <param name="AdminOnly">Quyền chỉ Admin có, không gán được cho vai trò khác.</param>
public sealed record RolePermissionsDto(IReadOnlyList<string> Catalog, IReadOnlyList<string> AdminOnly, IReadOnlyList<RolePermissionDto> Roles);

public interface IRolePermissionService
{
    /// <summary>Quyền đang áp dụng của một vai trò, đọc từ DB. Admin luôn đủ, User không có.</summary>
    Task<OpsCapability> ForAsync(UserRole role, CancellationToken ct = default);

    Task<RolePermissionsDto> ListAsync(CancellationToken ct = default);

    Task<RolePermissionDto> SaveAsync(Guid adminId, UserRole role, IReadOnlyList<string>? capabilities, string? reason, string? ip, CancellationToken ct = default);
}

public sealed class RolePermissionService : IRolePermissionService
{
    private const int MaxReasonLength = 500;

    private readonly IRolePermissionRepository _repository;
    private readonly IAuditLogService _audit;

    public RolePermissionService(IRolePermissionRepository repository, IAuditLogService audit)
    {
        _repository = repository;
        _audit = audit;
    }

    public async Task<OpsCapability> ForAsync(UserRole role, CancellationToken ct = default)
    {
        if (!RolePermissionRules.EditableRoles.Contains(role))
        {
            return OpsPermissions.For(role);
        }

        var row = await _repository.GetAsync(role, ct);
        return row is null ? OpsPermissions.For(role) : Clean(row.Capabilities);
    }

    public async Task<RolePermissionsDto> ListAsync(CancellationToken ct = default)
    {
        var roles = new List<RolePermissionDto>();
        foreach (var role in new[] { UserRole.User, UserRole.Staff, UserRole.OpsManager, UserRole.Admin })
        {
            roles.Add(new RolePermissionDto(role.ToString(), RolePermissionRules.EditableRoles.Contains(role), OpsPermissions.Names(await ForAsync(role, ct))));
        }

        return new RolePermissionsDto(RolePermissionRules.CatalogNames, OpsPermissions.Names(RolePermissionRules.AdminOnly), roles);
    }

    public async Task<RolePermissionDto> SaveAsync(Guid adminId, UserRole role, IReadOnlyList<string>? capabilities, string? reason, string? ip, CancellationToken ct = default)
    {
        if (!RolePermissionRules.EditableRoles.Contains(role))
        {
            throw new BusinessRuleException(RolePermissionErrorCodes.RoleNotEditable, "Chỉ chỉnh được quyền của Staff và OpsManager. Admin luôn có đủ quyền, User không có quyền nội bộ.");
        }

        var cleanReason = SupportTicketRules.RequireText(reason, MaxReasonLength, "lý do");
        var wanted = Parse(capabilities ?? []);
        if (wanted.HasFlag(RolePermissionRules.AdminOnly))
        {
            throw new BusinessRuleException(RolePermissionErrorCodes.AdminOnlyCapability, $"{RolePermissionRules.AdminOnly} chỉ dành cho Admin.");
        }

        var broken = RolePermissionRules.BrokenDependencies(wanted).ToList();
        if (broken.Count > 0)
        {
            throw new BusinessRuleException(RolePermissionErrorCodes.BrokenDependency, string.Join(" ", broken));
        }

        var before = await ForAsync(role, ct);
        if (before == wanted)
        {
            // Gửi lại trạng thái đang có thì không ghi gì, cùng cách với đổi vai trò người dùng.
            return new RolePermissionDto(role.ToString(), true, OpsPermissions.Names(before));
        }

        await _repository.SaveAsync(role, (long)wanted, adminId, ct);
        await _audit.LogAsync(
            AuditEvents.AdminRolePermissionsUpdated,
            adminId,
            ip,
            new
            {
                role = role.ToString(),
                added = OpsPermissions.Names(wanted & ~before),
                removed = OpsPermissions.Names(before & ~wanted),
                reason = cleanReason,
            },
            ct);

        return new RolePermissionDto(role.ToString(), true, OpsPermissions.Names(wanted));
    }

    private static OpsCapability Clean(long stored)
        => (OpsCapability)(int)(stored & (long)RolePermissionRules.All) & ~RolePermissionRules.AdminOnly;

    private static OpsCapability Parse(IReadOnlyList<string> names)
    {
        var known = Enum.GetNames<OpsCapability>().Where(n => n != nameof(OpsCapability.None)).ToHashSet();
        var result = OpsCapability.None;
        foreach (var name in names)
        {
            if (!known.Contains(name))
            {
                throw new BusinessRuleException(RolePermissionErrorCodes.UnknownCapability, $"Không có quyền tên '{name}'.");
            }

            result |= Enum.Parse<OpsCapability>(name);
        }

        return result;
    }
}
