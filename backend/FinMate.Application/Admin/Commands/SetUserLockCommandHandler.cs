using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class SetUserLockCommandHandler : ISetUserLockCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<SetUserLockCommand> _validator;
    private readonly ISupportRepository? _support;

    public SetUserLockCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IAuditLogService auditLogService,
        IValidator<SetUserLockCommand> validator,
        ISupportRepository? support = null)
    {
        _support = support;
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminUserDto> HandleAsync(
        SetUserLockCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        if (command.IsLocked && command.AdminId == command.TargetUserId)
        {
            throw new BusinessRuleException(
                AdminErrorCodes.CannotLockSelf,
                "Không thể tự khóa tài khoản của chính mình.");
        }

        var user = await _userRepository.GetByIdAsync(command.TargetUserId, ct)
            ?? throw new NotFoundException("User", command.TargetUserId);

        if (user.IsLocked == command.IsLocked)
        {
            // PATCH nhận TRẠNG THÁI mong muốn nên gọi lại vẫn hợp lệ. Không ghi audit cho lần
            // gọi không đổi gì — nhật ký đầy những dòng "khóa một tài khoản đã khóa" thì mất
            // tác dụng của chính nó.
            return AdminMapper.ToDto(user);
        }

        user.IsLocked = command.IsLocked;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);

        if (command.IsLocked)
        {
            // Vai trò và danh tính nằm trong access token, không tra lại DB mỗi request, nên
            // khóa tài khoản KHÔNG vô hiệu hóa token đang cầm — người dùng còn vào được tới
            // hết TTL (15 phút). Thu hồi refresh token là thứ chặn họ gia hạn thêm; đó là biên
            // trên của cửa sổ, và là đánh đổi có sẵn của thiết kế JWT hiện tại.
            await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);

            // Có thể chính kẻ chiếm tài khoản đã bấm đồng ý, nên mở khóa sau đó không được làm quyền cũ sống lại.
            if (_support is not null)
            {
                await _support.EndGrantsForRequesterAsync(user.Id, user.UpdatedAt, ct);
            }
        }

        await _auditLogService.LogAsync(
            command.IsLocked ? AuditEvents.AdminUserLocked : AuditEvents.AdminUserUnlocked,
            command.AdminId,
            command.IpAddress,
            new { targetUserId = user.Id, reason = command.Reason?.Trim() },
            ct);

        return AdminMapper.ToDto(user);
    }
}
