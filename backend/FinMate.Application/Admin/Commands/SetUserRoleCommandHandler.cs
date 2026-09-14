using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class SetUserRoleCommandHandler : ISetUserRoleCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<SetUserRoleCommand> _validator;

    public SetUserRoleCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogService auditLogService,
        IValidator<SetUserRoleCommand> validator)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminUserDto> HandleAsync(
        SetUserRoleCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Chặn TRƯỚC khi nạp user: kể cả thăng chính mình cũng chặn, vì một admin tự nâng
        // quyền cho mình thì audit log mất hẳn ý nghĩa "ai trao quyền cho ai".
        if (command.AdminId == command.TargetUserId)
        {
            throw new BusinessRuleException(
                AdminErrorCodes.CannotChangeOwnRole,
                "Không thể tự đổi vai trò của chính mình. Nhờ một admin khác thực hiện.");
        }

        var user = await _userRepository.GetByIdAsync(command.TargetUserId, ct)
            ?? throw new NotFoundException("User", command.TargetUserId);

        if (user.Role == command.Role)
        {
            // PATCH nhận TRẠNG THÁI mong muốn nên gọi lại vẫn hợp lệ. Không ghi audit cho lần
            // gọi không đổi gì — cùng lý do với SetUserLockCommandHandler.
            return AdminMapper.ToDto(user);
        }

        if (command.Role == UserRole.Admin && user.DeletedAt is not null)
        {
            throw new BusinessRuleException(
                AdminErrorCodes.CannotPromoteDeletedUser,
                "Tài khoản này đang chờ xoá. Huỷ yêu cầu xoá trước khi cấp quyền quản trị.");
        }

        if (command.Role == UserRole.User && await _userRepository.CountActiveAdminsAsync(ct) <= 1)
        {
            // Đếm admin còn HOẠT ĐỘNG (chưa bị khoá). Hạ nốt người cuối là hệ thống không còn
            // ai quản trị được, mà đường cứu duy nhất là sửa ADMIN_SEED_* rồi khởi động lại
            // service — không làm được qua API.
            throw new BusinessRuleException(
                AdminErrorCodes.CannotDemoteLastAdmin,
                "Đây là admin hoạt động cuối cùng. Cấp quyền cho người khác trước khi hạ quyền tài khoản này.");
        }

        var previousRole = user.Role;
        user.Role = command.Role;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        if (command.Role == UserRole.User)
        {
            // Vai trò nằm TRONG access token và không được tra lại DB mỗi request, nên hạ quyền
            // KHÔNG có hiệu lực ngay: token cũ vẫn ghi Admin tới hết TTL 15 phút. Thu hồi
            // refresh token chặn họ gia hạn, kẹp cửa sổ leo thang đặc quyền ở đúng 15 phút đó.
            // Cùng đánh đổi đã ghi ở SetUserLockCommandHandler.
            await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);
        }

        // Thăng quyền không cần thu hồi: RefreshTokenCommandHandler nạp lại user từ DB mỗi lần
        // refresh, nên vai trò mới tự có ở lần refresh kế tiếp mà không phải đá họ ra.

        await _auditLogService.LogAsync(
            AuditEvents.AdminUserRoleChanged,
            command.AdminId,
            command.IpAddress,
            new
            {
                targetUserId = user.Id,
                from = previousRole.ToString(),
                to = user.Role.ToString(),
                reason = command.Reason?.Trim(),
            },
            ct);

        return AdminMapper.ToDto(user);
    }
}
