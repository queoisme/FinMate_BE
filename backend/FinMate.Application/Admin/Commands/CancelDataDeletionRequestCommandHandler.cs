using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

/// <summary>
/// Dừng một yêu cầu xoá trước khi <c>DataDeletionJob</c> chạm vào, và khôi phục tài khoản.
///
/// Đây là đường cứu DUY NHẤT: <c>DeleteAccountCommandHandler</c> soft-delete người dùng ngay
/// lúc họ gửi yêu cầu, nên họ không đăng nhập lại được và không tự huỷ được yêu cầu của chính
/// mình. Không có endpoint này thì đổi ý đồng nghĩa với mất sạch dữ liệu.
/// </summary>
public class CancelDataDeletionRequestCommandHandler : ICancelDataDeletionRequestCommandHandler
{
    private readonly IDataDeletionRequestRepository _requestRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<CancelDataDeletionRequestCommand> _validator;

    public CancelDataDeletionRequestCommandHandler(
        IDataDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        IAuditLogService auditLogService,
        IValidator<CancelDataDeletionRequestCommand> validator)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<DataDeletionRequestDto> HandleAsync(
        CancelDataDeletionRequestCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var request = await _requestRepository.GetByIdAsync(command.RequestId, ct)
            ?? throw new NotFoundException("DataDeletionRequest", command.RequestId);

        // IgnoreQueryFilters: global filter deleted_at IS NULL giấu đúng dòng cần sửa.
        var user = await _userRepository.GetByIdIncludingDeletedAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        if (request.Status == DataDeletionStatus.Processed)
        {
            // ExecuteDeleteAsync đã chạy — không còn gì để khôi phục. Nói thẳng thay vì trả về
            // 204 rồi để admin tưởng đã cứu được người dùng.
            throw new BusinessRuleException(
                AdminErrorCodes.DeletionAlreadyProcessed,
                "Yêu cầu này đã xoá cứng xong, dữ liệu không còn để khôi phục.");
        }

        if (request.Status == DataDeletionStatus.Cancelled)
        {
            // Huỷ một yêu cầu đã huỷ không đổi gì; không ghi audit cho lần gọi không đổi gì,
            // cùng lý do với SetUserLockCommandHandler.
            return ToDto(request, user.Email, user.DisplayName);
        }

        var now = DateTimeOffset.UtcNow;

        request.Status = DataDeletionStatus.Cancelled;
        request.ProcessedAt = now;

        // Bước quan trọng nhất. Thiếu nó thì yêu cầu biến mất khỏi hàng đợi nhưng người dùng
        // vẫn bị soft delete, tức là vẫn không đăng nhập được — huỷ mà không cứu được ai.
        user.DeletedAt = null;
        user.UpdatedAt = now;

        // request và user cùng một scoped DbContext nên một lần lưu là atomic: không có trạng
        // thái lỡ dở kiểu "đã huỷ yêu cầu nhưng chưa khôi phục tài khoản".
        await _requestRepository.SaveChangesAsync(ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminDataDeletionCancelled,
            command.AdminId,
            command.IpAddress,
            new { requestId = request.Id, targetUserId = user.Id, reason = command.Reason?.Trim() },
            ct);

        return ToDto(request, user.Email, user.DisplayName);
    }

    private static DataDeletionRequestDto ToDto(
        Domain.Entities.DataDeletionRequest request, string email, string displayName)
        => new(
            request.Id,
            request.UserId,
            email,
            displayName,
            request.Status,
            request.RequestedAt,
            request.ScheduledHardDeleteAt,
            request.ProcessedAt,
            DaysUntilHardDelete: 0);
}
