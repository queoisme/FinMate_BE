using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.Groups.Bills;
using FinMate.Application.Groups.Funds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Groups.Approvals;

public sealed record GroupApprovalDto(
    Guid Id,
    Guid GroupId,
    GroupApprovalKind Kind,
    GroupApprovalStatus Status,
    long AmountCents,
    string Title,
    Guid? RequestedByUserId,
    string RequestedByName,
    DateTimeOffset CreatedAt,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    Guid? ResultId);

/// <summary>
/// Cổng duyệt giao dịch lớn. Nhóm đặt <c>requireApprovalAboveCents</c>; hóa đơn và chi/rút quỹ từ ngưỡng đó do người KHÔNG phải Owner tạo thì
/// được lưu chờ duyệt (chưa trừ quỹ, chưa vào ngân sách, báo cáo hay sổ nợ). Giao dịch cá nhân chia sẻ vào nhóm không đi qua đây: nó đã chi từ ví của chủ nó.
/// Các <c>TrySubmit*</c> trả null khi không cần duyệt: caller cứ tạo bình thường.
/// </summary>
public interface IGroupApprovalService
{
    Task<GroupApprovalDto?> TrySubmitBillAsync(CreateGroupBillCommand command, CancellationToken ct = default);
    Task<GroupApprovalDto?> TrySubmitFundEntryAsync(CreateGroupFundEntryCommand command, CancellationToken ct = default);
    /// <param name="status">Pending (mặc định khi bỏ trống), Approved, Rejected hoặc All.</param>
    Task<IReadOnlyList<GroupApprovalDto>> ListAsync(Guid groupId, Guid userId, string? status, int limit, CancellationToken ct = default);
    Task<GroupApprovalDto> ApproveAsync(Guid groupId, Guid userId, Guid approvalId, CancellationToken ct = default);
    Task<GroupApprovalDto> RejectAsync(Guid groupId, Guid userId, Guid approvalId, string? reason, CancellationToken ct = default);
}

public class GroupApprovalService : IGroupApprovalService
{
    public const int MaxListLimit = 100;
    public const int ReasonMaxLength = 200;

    private static readonly JsonSerializerOptions Json = CreateJson();

    private readonly IGroupApprovalRepository _approvals;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IGroupBillService _bills;
    private readonly IGroupFundEntryService _entries;
    private readonly ITransactionRepository _transactions;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly IAuditLogService _audit;
    private readonly ILogger<GroupApprovalService> _logger;

    public GroupApprovalService(
        IGroupApprovalRepository approvals,
        IGroupRepository groups,
        IGroupAccessService access,
        IGroupBillService bills,
        IGroupFundEntryService entries,
        ITransactionRepository transactions,
        IForecastAlertDispatcher dispatcher,
        IAuditLogService audit,
        ILogger<GroupApprovalService> logger)
    {
        _approvals = approvals;
        _groups = groups;
        _access = access;
        _bills = bills;
        _entries = entries;
        _transactions = transactions;
        _dispatcher = dispatcher;
        _audit = audit;
        _logger = logger;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public async Task<GroupApprovalDto?> TrySubmitBillAsync(CreateGroupBillCommand command, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ShareTransactions, ct);
        if (!Needs(access, command.AmountCents))
        {
            return null;
        }

        var title = command.Title.Trim();
        return await SubmitAsync(access.Group, command.UserId, GroupApprovalKind.Bill, command.AmountCents, title, JsonSerializer.Serialize(command, Json), null, ct);
    }

    public async Task<GroupApprovalDto?> TrySubmitFundEntryAsync(CreateGroupFundEntryCommand command, CancellationToken ct = default)
    {
        // Chỉ chi và rút lấy tiền ra khỏi quỹ; góp và điều chỉnh đi thẳng (điều chỉnh vốn đã chỉ người có ManageFunds làm được).
        if (command.Kind is not (GroupFundEntryKind.Expense or GroupFundEntryKind.Withdrawal))
        {
            return null;
        }

        var access = await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.RecordFundEntries, ct);
        var amount = command.AmountCents;
        if (command.FromTransactionId is { } sourceId)
        {
            // Số tiền của dòng chuyển đổi là số tiền của giao dịch gốc: không lấy từ body, nếu không đổi sang chuyển đổi là lách được ngưỡng.
            amount = (await _transactions.GetByIdAsync(sourceId, command.UserId, ct))?.AmountCents ?? 0;
        }

        if (!Needs(access, amount))
        {
            return null;
        }

        if (command.ClientRequestId is { } requestId
            && await _approvals.GetByClientRequestAsync(command.GroupId, command.UserId, requestId, ct) is { } existing)
        {
            return await ToDtoAsync(existing, ct);
        }

        var title = !string.IsNullOrWhiteSpace(command.MerchantName) ? command.MerchantName!.Trim()
            : !string.IsNullOrWhiteSpace(command.Note) ? command.Note!.Trim()
            : command.Kind == GroupFundEntryKind.Withdrawal ? "Rút quỹ" : "Chi từ quỹ";
        return await SubmitAsync(access.Group, command.UserId, GroupApprovalKind.FundEntry, amount, Cut(title, 200), JsonSerializer.Serialize(command, Json), command.ClientRequestId, ct);
    }

    public async Task<IReadOnlyList<GroupApprovalDto>> ListAsync(Guid groupId, Guid userId, string? status, int limit, CancellationToken ct = default)
    {
        GroupApprovalStatus? parsed = GroupApprovalStatus.Pending;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                parsed = null;
            }
            else if (Enum.TryParse<GroupApprovalStatus>(status, true, out var value))
            {
                parsed = value;
            }
            else
            {
                throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Status", "Trạng thái phải là Pending, Approved, Rejected hoặc All.")]);
            }
        }

        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        var rows = await _approvals.ListAsync(groupId, parsed, access.Member.Role == GroupRole.Owner ? null : userId, GroupPaging.Clamp(limit, MaxListLimit), ct);
        return rows.Select(r => ToDto(r.Approval, r.RequestedByName)).ToList();
    }

    public async Task<GroupApprovalDto> ApproveAsync(Guid groupId, Guid userId, Guid approvalId, CancellationToken ct = default)
    {
        await _access.RequireOwnerAsync(groupId, userId, ct);
        var approval = await RequirePendingAsync(groupId, approvalId, ct);
        var now = DateTimeOffset.UtcNow;
        approval.Status = GroupApprovalStatus.Approved;
        approval.DecidedByUserId = userId;
        approval.DecidedAt = now;

        // Chạy lại đúng đường tạo bình thường dưới tên người yêu cầu (quyền, thành viên, ví, danh mục được kiểm lại lúc này); lỗi thì giữ nguyên chờ duyệt.
        // Dịch vụ tạo lưu luôn thay đổi của yêu cầu vì cùng một DbContext.
        approval.ResultId = approval.Kind switch
        {
            GroupApprovalKind.Bill => (await _bills.CreateAsync(Deserialize<CreateGroupBillCommand>(approval), ct)).Id,
            _ => (await _entries.CreateAsync(Deserialize<CreateGroupFundEntryCommand>(approval), ct)).Entry.Id,
        };

        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.ApprovalApproved, approval.Id, now));
        await _approvals.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupApprovalDecided, userId, metadata: new { GroupId = groupId, ApprovalId = approvalId, Approved = true }, ct: ct);
        await NotifyRequesterAsync(approval, approved: true, ct);
        return await ToDtoAsync(approval, ct);
    }

    public async Task<GroupApprovalDto> RejectAsync(Guid groupId, Guid userId, Guid approvalId, string? reason, CancellationToken ct = default)
    {
        await _access.RequireOwnerAsync(groupId, userId, ct);
        if (reason is not null && reason.Length > ReasonMaxLength)
        {
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Reason", $"Lý do tối đa {ReasonMaxLength} ký tự.")]);
        }

        var approval = await RequirePendingAsync(groupId, approvalId, ct);
        var now = DateTimeOffset.UtcNow;
        approval.Status = GroupApprovalStatus.Rejected;
        approval.DecidedByUserId = userId;
        approval.DecidedAt = now;
        approval.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.ApprovalRejected, approval.Id, now));
        await _approvals.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupApprovalDecided, userId, metadata: new { GroupId = groupId, ApprovalId = approvalId, Approved = false }, ct: ct);
        await NotifyRequesterAsync(approval, approved: false, ct);
        return await ToDtoAsync(approval, ct);
    }

    private static bool Needs(GroupAccess access, long amountCents)
        => access.Member.Role != GroupRole.Owner
            && access.Group.RequireApprovalAboveCents is { } threshold
            && amountCents >= threshold;

    private async Task<GroupApproval> RequirePendingAsync(Guid groupId, Guid approvalId, CancellationToken ct)
    {
        var approval = await _approvals.GetAsync(groupId, approvalId, ct) ?? throw new NotFoundException("GroupApproval", approvalId);
        return approval.Status == GroupApprovalStatus.Pending
            ? approval
            : throw new BusinessRuleException(GroupErrorCodes.ApprovalNotPending, "Yêu cầu này đã được xử lý.");
    }

    private async Task<GroupApprovalDto> SubmitAsync(
        Group group, Guid userId, GroupApprovalKind kind, long amount, string title, string payload, Guid? clientRequestId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var approval = new GroupApproval
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Kind = kind,
            RequestedByUserId = userId,
            AmountCents = amount,
            Title = title,
            PayloadJson = payload,
            ClientRequestId = clientRequestId,
            CreatedAt = now,
        };
        _approvals.Add(approval);
        _groups.AddActivity(GroupService.NewActivity(group.Id, userId, GroupActivityType.ApprovalRequested, approval.Id, now));
        await _approvals.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupApprovalRequested, userId, metadata: new { GroupId = group.Id, ApprovalId = approval.Id }, ct: ct);
        await NotifyOwnerAsync(group, approval, ct);
        return await ToDtoAsync(approval, ct);
    }

    // Hai hàm gửi không bao giờ ném: yêu cầu đã lưu rồi. Không số tiền trong push.
    private async Task NotifyOwnerAsync(Group group, GroupApproval approval, CancellationToken ct)
    {
        try
        {
            var owner = (await _groups.ListActiveMembersAsync(group.Id, ct)).FirstOrDefault(m => m.Member.Role == GroupRole.Owner);
            if (owner is null || !owner.Member.AlertsEnabled)
            {
                return;
            }

            var who = await _approvals.GetDisplayNameAsync(approval.RequestedByUserId ?? Guid.Empty, ct) ?? "Một thành viên";
            await _dispatcher.RaiseAsync(
                new ForecastAlertRequest(
                    owner.Member.UserId,
                    ForecastAlertKind.GroupApprovalRequested,
                    $"group_approval_req:{approval.Id}",
                    "Có khoản cần bạn duyệt",
                    $"{who} gửi một khoản chờ duyệt trong nhóm {group.Name}.",
                    new Dictionary<string, string> { ["groupId"] = group.Id.ToString(), ["approvalId"] = approval.Id.ToString() }),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group approval push could not be sent for {GroupId}: {ExceptionType}", group.Id, ex.GetType().Name);
        }
    }

    private async Task NotifyRequesterAsync(GroupApproval approval, bool approved, CancellationToken ct)
    {
        try
        {
            if (approval.RequestedByUserId is not { } requester)
            {
                return;
            }

            var member = await _groups.GetActiveMemberAsync(approval.GroupId, requester, ct);
            var group = await _groups.GetByIdAsync(approval.GroupId, ct);
            if (member is null || group is null || !member.AlertsEnabled)
            {
                return;
            }

            await _dispatcher.RaiseAsync(
                new ForecastAlertRequest(
                    requester,
                    ForecastAlertKind.GroupApprovalDecided,
                    $"group_approval_res:{approval.Id}",
                    approved ? "Khoản của bạn đã được duyệt" : "Khoản của bạn bị từ chối",
                    approved ? $"Chủ nhóm {group.Name} đã duyệt khoản bạn gửi." : $"Chủ nhóm {group.Name} đã từ chối khoản bạn gửi.",
                    new Dictionary<string, string> { ["groupId"] = group.Id.ToString(), ["approvalId"] = approval.Id.ToString(), ["approved"] = approved ? "true" : "false" }),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group approval push could not be sent for {GroupId}: {ExceptionType}", approval.GroupId, ex.GetType().Name);
        }
    }

    private static T Deserialize<T>(GroupApproval approval)
        => JsonSerializer.Deserialize<T>(approval.PayloadJson, Json) ?? throw new InvalidOperationException("Approval payload is empty.");

    private async Task<GroupApprovalDto> ToDtoAsync(GroupApproval approval, CancellationToken ct)
        => ToDto(approval, approval.RequestedByUserId is { } id ? await _approvals.GetDisplayNameAsync(id, ct) : null);

    private static GroupApprovalDto ToDto(GroupApproval a, string? requestedByName)
        => new(a.Id, a.GroupId, a.Kind, a.Status, a.AmountCents, a.Title, a.RequestedByUserId, requestedByName ?? "Thành viên cũ",
            a.CreatedAt, a.DecidedByUserId, a.DecidedAt, a.DecisionReason, a.ResultId);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
