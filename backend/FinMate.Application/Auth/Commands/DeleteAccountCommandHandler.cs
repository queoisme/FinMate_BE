using FinMate.Application.Groups;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Auth.Commands;

public class DeleteAccountCommandHandler : IDeleteAccountCommandHandler
{
    private const int RetentionDays = 30;

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IDataDeletionRequestRepository _dataDeletionRequestRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IGroupOwnershipHandover? _groupHandover;
    private readonly ISupportRepository? _support;
    private readonly IOpsNotificationService? _opsNotifications;
    private readonly IPersonalDataExportRepository? _exports;

    public DeleteAccountCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IRefreshTokenRepository refreshTokenRepository,
        IDataDeletionRequestRepository dataDeletionRequestRepository,
        IAuditLogService auditLogService,
        IGroupOwnershipHandover? groupHandover = null,
        ISupportRepository? support = null,
        IOpsNotificationService? opsNotifications = null,
        IPersonalDataExportRepository? exports = null)
    {
        _opsNotifications = opsNotifications;
        _support = support;
        _exports = exports;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _refreshTokenRepository = refreshTokenRepository;
        _dataDeletionRequestRepository = dataDeletionRequestRepository;
        _auditLogService = auditLogService;
        _groupHandover = groupHandover;
    }

    public async Task HandleAsync(DeleteAccountCommand command, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException("User", command.UserId);

        if (user.PasswordHash is null)
        {
            throw new AuthenticationException(
                AuthErrorCodes.InvalidCredentials, "Tài khoản này chưa có mật khẩu (đăng nhập qua Google).");
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            throw new AuthenticationException(AuthErrorCodes.InvalidCredentials, "Mật khẩu không đúng.");
        }

        // Phase 91 (S5): gói dữ liệu đang dựng sẽ không tải được sau khi xin xoá (không đăng nhập được nữa), nên chờ nó xong trước.
        if (_exports is not null && await _exports.HasActiveAsync(user.Id, ct))
        {
            throw new ConflictException(DataExportErrorCodes.InProgress,
                "Gói dữ liệu của bạn đang được tạo. Hãy chờ tải về xong rồi mới xoá tài khoản.");
        }

        // Trước khi khóa tài khoản: hỏng ở đây thì chưa có gì thay đổi và người dùng thử lại được.
        if (_groupHandover is not null)
        {
            await _groupHandover.HandOverAsync(user.Id, ct);
        }

        var now = DateTimeOffset.UtcNow;

        var deletion = new DataDeletionRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RequestedAt = now,
            ScheduledHardDeleteAt = now.AddDays(RetentionDays),
        };
        await _dataDeletionRequestRepository.AddAsync(deletion, ct);

        user.DeletedAt = now;
        user.UpdatedAt = now;
        await _userRepository.UpdateAsync(user, ct);

        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, ct);

        // Họ không đăng nhập được nữa nên không tự thu hồi được; hủy yêu cầu xóa sau đó cũng không làm quyền cũ sống lại.
        if (_support is not null)
        {
            await _support.EndGrantsForRequesterAsync(user.Id, now, ct);
        }

        await _auditLogService.LogAsync(AuditEvents.AccountDeletionRequested, user.Id, ct: ct);

        // Hàng đợi xóa dữ liệu nằm dưới /admin nên chỉ người có ManageSystem được báo. Không có tên hay email: mở trang chi tiết mới thấy.
        if (_opsNotifications is not null)
        {
            await _opsNotifications.NotifyCapabilityAsync(
                OpsNotificationType.DataDeletionRequested,
                OpsCapability.ManageSystem,
                "Có yêu cầu xóa dữ liệu mới",
                "Một khách vừa yêu cầu xóa tài khoản và dữ liệu. Xem hàng đợi xóa dữ liệu.",
                "DataDeletionRequest",
                deletion.Id,
                new[] { user.Id },
                ct);
        }
    }
}
