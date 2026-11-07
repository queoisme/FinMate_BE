using FinMate.Application.Admin;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Ops;

public static class OpsErrorCodes
{
    public const string InvalidInput = "OPS_INVALID_INPUT";
    public const string ApprovalAlreadyPending = "OPS_APPROVAL_ALREADY_PENDING";
    public const string ApprovalNotPending = "OPS_APPROVAL_NOT_PENDING";
    public const string ApprovalExpired = "OPS_APPROVAL_EXPIRED";
    public const string OwnApproval = "OPS_OWN_APPROVAL";
    public const string InvalidTarget = "OPS_INVALID_TARGET";
    public const string ApprovalFailed = "OPS_APPROVAL_FAILED";

    /// <summary>Đã duyệt nhưng bước thực hiện gặp lỗi hệ thống. Người duyệt phải kiểm tra lại đối tượng rồi đề xuất lại nếu cần.</summary>
    public const string ApprovalExecutionError = "OPS_APPROVAL_EXECUTION_ERROR";
    public const string OtpEmailFailed = "OPS_OTP_EMAIL_FAILED";
}

public sealed record ProposeApprovalCommand(OpsApprovalType Type, Guid TargetId, Guid? NewOwnerUserId, Guid? TicketId, string? Reason);

public record OpsApprovalDto(
    Guid Id,
    string Type,
    Guid TargetId,
    Guid? NewOwnerUserId,
    Guid? TicketId,
    string Reason,
    Guid? RequestedByUserId,
    string Status,
    bool Expired,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string? FailureCode,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public interface IOpsApprovalService
{
    Task<OpsApprovalDto> ProposeAsync(Guid actorId, ProposeApprovalCommand command, string? ip, CancellationToken ct = default);

    /// <summary>Người duyệt thấy tất cả; người chỉ có quyền đề xuất chỉ thấy đề xuất của mình.</summary>
    Task<PagedResult<OpsApprovalDto>> ListAsync(Guid actorId, OpsApprovalStatus? status, string? search, int offset, int limit, CancellationToken ct = default);
    Task<OpsApprovalDto> ApproveAsync(Guid actorId, Guid approvalId, string? note, string? ip, CancellationToken ct = default);
    Task<OpsApprovalDto> RejectAsync(Guid actorId, Guid approvalId, string? note, string? ip, CancellationToken ct = default);
}

public sealed class OpsApprovalService : IOpsApprovalService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private const int MaxReasonLength = 1000;

    private readonly IOpsRepository _ops;
    private readonly IOpsAccessService _access;
    private readonly IUserRepository _users;
    private readonly IDataDeletionRequestRepository _deletions;
    private readonly IGroupRepository _groups;
    private readonly ISupportRepository _support;
    private readonly ISetUserLockCommandHandler _lockHandler;
    private readonly ICancelDataDeletionRequestCommandHandler _cancelDeletionHandler;
    private readonly IGroupSupportTransfer _groupTransfer;
    private readonly IAuditLogService _audit;
    private readonly ISystemConfigProvider _config;

    public OpsApprovalService(
        IOpsRepository ops,
        IOpsAccessService access,
        IUserRepository users,
        IDataDeletionRequestRepository deletions,
        IGroupRepository groups,
        ISupportRepository support,
        ISetUserLockCommandHandler lockHandler,
        ICancelDataDeletionRequestCommandHandler cancelDeletionHandler,
        IGroupSupportTransfer groupTransfer,
        IAuditLogService audit,
        ISystemConfigProvider config)
    {
        _config = config;
        _ops = ops;
        _access = access;
        _users = users;
        _deletions = deletions;
        _groups = groups;
        _support = support;
        _lockHandler = lockHandler;
        _cancelDeletionHandler = cancelDeletionHandler;
        _groupTransfer = groupTransfer;
        _audit = audit;
    }

    public async Task<OpsApprovalDto> ProposeAsync(Guid actorId, ProposeApprovalCommand command, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ProposeApprovals, ct);
        var reason = SupportTicketRules.RequireText(command.Reason, MaxReasonLength, "lý do");

        if (command.TicketId is { } ticketId)
        {
            var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
            if (ticket.RequesterUserId == actor.Id
                || (ticket.AssigneeUserId != actor.Id && !await _access.HasAsync(actor.Role, OpsCapability.ManageTickets, ct)))
            {
                throw new ForbiddenException("Bạn không phụ trách yêu cầu này.");
            }

            // Ca dẫn chứng phải đúng là của khách được đề xuất, để người duyệt không bị dẫn tới một căn cứ không liên quan.
            if (command.Type != OpsApprovalType.TransferGroupOwner && ticket.RequesterUserId != command.TargetId)
            {
                throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Yêu cầu hỗ trợ này không phải của tài khoản được đề xuất.");
            }
        }

        // Không ai đề xuất việc có lợi cho chính mình.
        if (command.TargetId == actor.Id || command.NewOwnerUserId == actor.Id)
        {
            throw new ForbiddenException("Bạn không thể đề xuất việc liên quan đến chính mình.");
        }

        await EnsureTargetValidAsync(command.Type, command.TargetId, command.NewOwnerUserId, ct);

        var now = DateTimeOffset.UtcNow;

        // Đề xuất quá hạn không duyệt được nữa, nên phải giải phóng để không chặn đề xuất mới cho cùng đối tượng mãi mãi.
        if (await _ops.GetPendingApprovalAsync(command.Type, command.TargetId, ct) is { } stale && stale.ExpiresAt <= now)
        {
            await _ops.MarkApprovalFailedAsync(stale.Id, OpsErrorCodes.ApprovalExpired, ct);
        }

        var approval = new OpsApprovalRequest
        {
            Id = Guid.NewGuid(),
            Type = command.Type,
            TargetId = command.TargetId,
            NewOwnerUserId = command.NewOwnerUserId,
            TicketId = command.TicketId,
            Reason = reason,
            RequestedByUserId = actor.Id,
            Status = OpsApprovalStatus.Pending,
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
        };
        await _ops.AddApprovalAsync(approval, ct);
        await _audit.LogAsync(AuditEvents.OpsApprovalProposed, actor.Id, ip, new { approvalId = approval.Id, type = command.Type.ToString(), targetId = command.TargetId }, ct);
        return ToDto(approval, now);
    }

    public async Task<PagedResult<OpsApprovalDto>> ListAsync(Guid actorId, OpsApprovalStatus? status, string? search, int offset, int limit, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ProposeApprovals, ct);
        var requestedBy = await _access.HasAsync(actor.Role, OpsCapability.DecideApprovals, ct) ? (Guid?)null : actor.Id;
        var now = DateTimeOffset.UtcNow;
        var (skip, take) = AdminPaging.Normalize(offset, limit);
        var (items, total) = await _ops.ListApprovalsAsync(status, requestedBy, search, skip, take, ct);
        return new PagedResult<OpsApprovalDto>(items.Select(a => ToDto(a, now)).ToList(), total, skip, take);
    }

    public async Task<OpsApprovalDto> ApproveAsync(Guid actorId, Guid approvalId, string? note, string? ip, CancellationToken ct = default)
    {
        var (actor, approval, now) = await RequireDecidableAsync(actorId, approvalId, ct);

        // Kiểm tra lại: đối tượng có thể đã đổi trong lúc chờ duyệt.
        if (approval.TargetId == actor.Id || approval.NewOwnerUserId == actor.Id)
        {
            throw new ForbiddenException("Bạn không thể duyệt việc liên quan đến chính mình.");
        }

        approval.Status = OpsApprovalStatus.Approved;
        approval.DecidedByUserId = actor.Id;
        approval.DecidedAt = now;
        approval.DecisionNote = CleanNote(note);

        // Lưu trước khi thực hiện: khóa xmin bảo đảm hai người duyệt cùng lúc thì chỉ một người đi tiếp, nên việc không chạy hai lần.
        await _ops.SaveChangesAsync(ct);

        // Đã qua bước chốt thì làm cho xong: người duyệt đóng tab giữa chừng không được bỏ dở một thay đổi trên tài khoản khách.
        try
        {
            await EnsureTargetValidAsync(approval.Type, approval.TargetId, approval.NewOwnerUserId, CancellationToken.None);
            await ExecuteAsync(actor.Id, approval, ip, CancellationToken.None);
        }
        catch (FinMateException ex)
        {
            await _ops.MarkApprovalFailedAsync(approvalId, ex.ErrorCode, CancellationToken.None);
            await _audit.LogAsync(AuditEvents.OpsApprovalFailed, actor.Id, ip, new { approvalId, code = ex.ErrorCode }, CancellationToken.None);
            throw new BusinessRuleException(OpsErrorCodes.ApprovalFailed, "Đã duyệt nhưng không thực hiện được: đối tượng không còn ở trạng thái phù hợp.");
        }
        catch (Exception)
        {
            // Không để hồ sơ ghi "đã duyệt" cho một việc có thể chưa xảy ra.
            await _ops.MarkApprovalFailedAsync(approvalId, OpsErrorCodes.ApprovalExecutionError, CancellationToken.None);
            await _audit.LogAsync(AuditEvents.OpsApprovalFailed, actor.Id, ip, new { approvalId, code = OpsErrorCodes.ApprovalExecutionError }, CancellationToken.None);
            throw;
        }

        await _audit.LogAsync(AuditEvents.OpsApprovalApproved, actor.Id, ip, new { approvalId, type = approval.Type.ToString(), targetId = approval.TargetId }, CancellationToken.None);
        return ToDto(approval, now);
    }

    public async Task<OpsApprovalDto> RejectAsync(Guid actorId, Guid approvalId, string? note, string? ip, CancellationToken ct = default)
    {
        var (actor, approval, now) = await RequireDecidableAsync(actorId, approvalId, ct);
        approval.Status = OpsApprovalStatus.Rejected;
        approval.DecidedByUserId = actor.Id;
        approval.DecidedAt = now;
        approval.DecisionNote = CleanNote(note);
        await _ops.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsApprovalRejected, actor.Id, ip, new { approvalId, type = approval.Type.ToString(), targetId = approval.TargetId }, ct);
        return ToDto(approval, now);
    }

    private async Task<(User Actor, OpsApprovalRequest Approval, DateTimeOffset Now)> RequireDecidableAsync(Guid actorId, Guid approvalId, CancellationToken ct)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.DecideApprovals, ct);
        var approval = await _ops.GetApprovalAsync(approvalId, ct) ?? throw new NotFoundException("OpsApprovalRequest", approvalId);

        if (approval.RequestedByUserId == actor.Id)
        {
            throw new ForbiddenException("Bạn không thể tự duyệt đề xuất của mình.");
        }

        if (approval.Status != OpsApprovalStatus.Pending)
        {
            throw new BusinessRuleException(OpsErrorCodes.ApprovalNotPending, "Đề xuất này đã được xử lý.");
        }

        var now = DateTimeOffset.UtcNow;
        if (approval.ExpiresAt <= now)
        {
            throw new BusinessRuleException(OpsErrorCodes.ApprovalExpired, "Đề xuất này đã hết hạn.");
        }

        return (actor, approval, now);
    }

    private async Task EnsureTargetValidAsync(OpsApprovalType type, Guid targetId, Guid? newOwnerUserId, CancellationToken ct)
    {
        switch (type)
        {
            case OpsApprovalType.UnlockUser:
                var locked = await _users.GetByIdAsync(targetId, ct);
                if (locked is null || locked.Role != UserRole.User || !locked.IsLocked)
                {
                    throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Chỉ mở khóa được tài khoản khách đang bị khóa.");
                }

                break;

            case OpsApprovalType.CancelDeletion:
                var pending = await _deletions.GetPendingForUserAsync(targetId, ct);
                if (pending is null)
                {
                    throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Tài khoản này không có yêu cầu xóa đang chờ.");
                }

                break;

            case OpsApprovalType.TransferGroupOwner:
                if (!(await _config.GetAsync(ct)).Document.Features.Groups)
                {
                    throw new BusinessRuleException(GroupErrorCodes.Disabled, "Tính năng nhóm hiện chưa được bật.");
                }

                if (newOwnerUserId is not { } newOwner
                    || await _groups.GetByIdAsync(targetId, ct) is null
                    || await _groups.GetActiveMemberAsync(targetId, newOwner, ct) is not { Role: not GroupRole.Owner })
                {
                    throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Người nhận phải là thành viên đang hoạt động và chưa là chủ nhóm.");
                }

                break;

            default:
                throw new BusinessRuleException(OpsErrorCodes.InvalidInput, "Loại đề xuất không hợp lệ.");
        }
    }

    private async Task ExecuteAsync(Guid actorId, OpsApprovalRequest approval, string? ip, CancellationToken ct)
    {
        switch (approval.Type)
        {
            case OpsApprovalType.UnlockUser:
                await _lockHandler.HandleAsync(new SetUserLockCommand(actorId, approval.TargetId, false, $"Đề xuất {approval.Id}", ip), ct);
                break;

            case OpsApprovalType.CancelDeletion:
                var pending = await _deletions.GetPendingForUserAsync(approval.TargetId, ct)
                    ?? throw new BusinessRuleException(OpsErrorCodes.InvalidTarget, "Tài khoản này không có yêu cầu xóa đang chờ.");
                await _cancelDeletionHandler.HandleAsync(new CancelDataDeletionRequestCommand(actorId, pending.Id, $"Đề xuất {approval.Id}", ip), ct);
                break;

            case OpsApprovalType.TransferGroupOwner:
                await _groupTransfer.TransferAsync(actorId, approval.TargetId, approval.NewOwnerUserId!.Value, ip, ct);
                break;
        }
    }

    private static string? CleanNote(string? note)
    {
        var clean = note?.Trim();
        if (clean?.Length > MaxReasonLength)
        {
            throw new BusinessRuleException(OpsErrorCodes.InvalidInput, $"Ghi chú tối đa {MaxReasonLength} ký tự.");
        }

        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    private static OpsApprovalDto ToDto(OpsApprovalRequest a, DateTimeOffset now)
        => new(
            a.Id,
            a.Type.ToString(),
            a.TargetId,
            a.NewOwnerUserId,
            a.TicketId,
            a.Reason,
            a.RequestedByUserId,
            a.Status.ToString(),
            a.Status == OpsApprovalStatus.Pending && a.ExpiresAt <= now,
            a.DecidedByUserId,
            a.DecidedAt,
            a.DecisionNote,
            a.FailureCode,
            a.ExpiresAt,
            a.CreatedAt);
}
