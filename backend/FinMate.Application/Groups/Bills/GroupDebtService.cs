using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Groups.Bills;

/// <param name="NetCents">Dương = đang được nợ, âm = đang nợ.</param>
public sealed record GroupBalanceMemberDto(Guid UserId, string DisplayName, bool IsActive, long NetCents);

public sealed record GroupTransferDto(Guid FromUserId, Guid ToUserId, long AmountCents);

/// <param name="Transfers">Null khi nhóm chọn cấn trừ <c>Manual</c>: người dùng tự quyết ai trả ai.</param>
/// <param name="FormerMembersNetCents">Phần của người đã xóa cứng tài khoản; null khi không có.</param>
public sealed record GroupBalancesDto(
    DebtSettlementMethod Method,
    IReadOnlyList<GroupBalanceMemberDto> Members,
    IReadOnlyList<GroupTransferDto>? Transfers,
    long? FormerMembersNetCents);

public sealed record GroupSettlementDto(Guid Id, Guid FromUserId, Guid ToUserId, long AmountCents, string? Note, DateTimeOffset CreatedAt);

public sealed record CreateGroupSettlementCommand(Guid UserId, Guid GroupId, Guid FromUserId, Guid ToUserId, long AmountCents, string? Note);

public sealed record GroupRemindSkippedDto(Guid UserId, DateTimeOffset RetryAt);

public sealed record GroupRemindResultDto(IReadOnlyList<Guid> Sent, IReadOnlyList<GroupRemindSkippedDto> Skipped);

public class CreateGroupSettlementCommandValidator : AbstractValidator<CreateGroupSettlementCommand>
{
    public const int NoteMaxLength = 200;

    public CreateGroupSettlementCommandValidator()
    {
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.Note).MaximumLength(NoteMaxLength).WithMessage($"Ghi chú tối đa {NoteMaxLength} ký tự.");
    }
}

public interface IGroupDebtService
{
    Task<GroupBalancesDto> GetBalancesAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupSettlementDto> SettleAsync(CreateGroupSettlementCommand command, CancellationToken ct = default);
    Task<GroupRemindResultDto> RemindAsync(Guid groupId, Guid userId, Guid debtorUserId, CancellationToken ct = default);
    Task<GroupRemindResultDto> RemindAllAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

public class GroupDebtService : IGroupDebtService
{
    private readonly IGroupBillRepository _bills;
    private readonly IGroupRepository _groups;
    private readonly IUserRepository _users;
    private readonly IGroupAccessService _access;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;
    private readonly IValidator<CreateGroupSettlementCommand> _validator;
    private readonly ILogger<GroupDebtService> _logger;

    public GroupDebtService(
        IGroupBillRepository bills,
        IGroupRepository groups,
        IUserRepository users,
        IGroupAccessService access,
        IForecastAlertDispatcher dispatcher,
        ISystemConfigProvider config,
        IAuditLogService audit,
        IValidator<CreateGroupSettlementCommand> validator,
        ILogger<GroupDebtService> logger)
    {
        _bills = bills;
        _groups = groups;
        _users = users;
        _access = access;
        _dispatcher = dispatcher;
        _config = config;
        _audit = audit;
        _validator = validator;
        _logger = logger;
    }

    public async Task<GroupBalancesDto> GetBalancesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var (net, members) = await LoadAsync(groupId, ct);
        var rows = new List<GroupBalanceMemberDto>();
        foreach (var (id, view) in members)
        {
            rows.Add(new GroupBalanceMemberDto(id, view.IsUserActive ? view.DisplayName : GroupMemberService.InactiveMemberName, view.IsUserActive, net.GetValueOrDefault(id)));
        }

        // Người đã rời nhóm mà còn nợ vẫn phải hiện: số nợ không biến mất khi họ đi.
        foreach (var (id, value) in net.Where(p => p.Key != GroupDebtMath.Former && !members.ContainsKey(p.Key) && p.Value != 0))
        {
            var user = await _users.GetByIdIncludingDeletedAsync(id, ct);
            rows.Add(new GroupBalanceMemberDto(id, user?.DisplayName ?? GroupMemberService.InactiveMemberName, false, value));
        }

        var transfers = access.Group.DebtSettlementMethod == DebtSettlementMethod.AiOptimized
            ? GroupDebtMath.MinimalTransfers(net).Select(t => new GroupTransferDto(t.FromUserId, t.ToUserId, t.AmountCents)).ToList()
            : null;
        return new GroupBalancesDto(access.Group.DebtSettlementMethod, rows.OrderBy(r => r.NetCents).ThenBy(r => r.DisplayName).ToList(), transfers,
            net.TryGetValue(GroupDebtMath.Former, out var former) && former != 0 ? former : null);
    }

    public async Task<GroupSettlementDto> SettleAsync(CreateGroupSettlementCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var access = await _access.RequireMemberAsync(command.GroupId, command.UserId, ct);
        if (command.FromUserId == command.ToUserId)
        {
            throw new BusinessRuleException(GroupErrorCodes.SettlementInvalid, "Người trả và người nhận phải khác nhau.");
        }

        // Quyền: chính người trả, chính người nhận, hoặc Owner.
        if (command.UserId != command.FromUserId && command.UserId != command.ToUserId && access.Member.Role != GroupRole.Owner)
        {
            throw new ForbiddenException("Chỉ người trả, người nhận hoặc chủ nhóm mới ghi nhận được khoản trả nợ.");
        }

        var (net, members) = await LoadAsync(command.GroupId, ct);
        if (!members.ContainsKey(command.FromUserId) || !members.ContainsKey(command.ToUserId))
        {
            throw new BusinessRuleException(GroupErrorCodes.SettlementInvalid, "Cả hai bên phải là thành viên đang hoạt động của nhóm.");
        }

        // ponytail: kiểm số nợ rồi mới ghi, không khóa: hai lần ghi đồng thời cùng một khoản có thể vượt nợ một lần; thêm khóa theo nhóm nếu thực tế xảy ra.
        var owes = Math.Max(0, -net.GetValueOrDefault(command.FromUserId));
        var owed = Math.Max(0, net.GetValueOrDefault(command.ToUserId));
        if (command.AmountCents > owes || command.AmountCents > owed)
        {
            throw new BusinessRuleException(GroupErrorCodes.SettlementExceedsDebt, "Số tiền vượt quá khoản đang nợ.");
        }

        var now = DateTimeOffset.UtcNow;
        var settlement = new GroupSettlement
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            FromUserId = command.FromUserId,
            ToUserId = command.ToUserId,
            AmountCents = command.AmountCents,
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            CreatedByUserId = command.UserId,
            CreatedAt = now,
        };
        _bills.AddSettlement(settlement);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.SettlementRecorded, settlement.Id, now));
        await _bills.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupSettlementRecorded, command.UserId, metadata: new { command.GroupId, SettlementId = settlement.Id }, ct: ct);
        return new GroupSettlementDto(settlement.Id, command.FromUserId, command.ToUserId, command.AmountCents, settlement.Note, now);
    }

    public async Task<GroupRemindResultDto> RemindAsync(Guid groupId, Guid userId, Guid debtorUserId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        var (net, members) = await LoadAsync(groupId, ct);
        if (!members.ContainsKey(debtorUserId) || net.GetValueOrDefault(debtorUserId) >= 0)
        {
            throw new BusinessRuleException(GroupErrorCodes.NoDebt, "Người này hiện không nợ ai trong nhóm.");
        }

        EnsureMayRemind(access, userId, net);
        var result = await SendAsync(access, userId, [debtorUserId], ct);
        if (result.Sent.Count == 0)
        {
            var retry = result.Skipped[0].RetryAt;
            throw new TooManyRequestsException(GroupErrorCodes.ReminderCooldown, "Bạn vừa nhắc người này rồi. Hãy thử lại sau.", retry);
        }

        return result;
    }

    public async Task<GroupRemindResultDto> RemindAllAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        var (net, members) = await LoadAsync(groupId, ct);
        EnsureMayRemind(access, userId, net);
        var debtors = net.Where(p => members.ContainsKey(p.Key) && p.Value < 0).Select(p => p.Key).ToList();
        var result = await SendAsync(access, userId, debtors, ct);
        if (debtors.Count > 0 && result.Sent.Count == 0)
        {
            throw new TooManyRequestsException(GroupErrorCodes.ReminderCooldown, "Mọi người đều đã được nhắc gần đây. Hãy thử lại sau.", result.Skipped.Min(s => s.RetryAt));
        }

        return result;
    }

    /// <summary>Chủ nợ (đang được nợ) hoặc người có quyền quản lý giao dịch chung mới nhắc được.</summary>
    private static void EnsureMayRemind(GroupAccess access, Guid userId, Dictionary<Guid, long> net)
    {
        if (!access.Has(GroupPermission.ManageSharedTransactions) && net.GetValueOrDefault(userId) <= 0)
        {
            throw new ForbiddenException("Chỉ người đang được nợ hoặc người quản lý nhóm mới nhắc nợ được.");
        }
    }

    private async Task<GroupRemindResultDto> SendAsync(GroupAccess access, Guid senderId, IReadOnlyList<Guid> debtors, CancellationToken ct)
    {
        var cooldown = TimeSpan.FromHours((await _config.GetAsync(ct)).Document.Groups.DebtReminderCooldownHours);
        var now = DateTimeOffset.UtcNow;
        var sent = new List<Guid>();
        var skipped = new List<GroupRemindSkippedDto>();
        var reminders = new List<GroupDebtReminder>();

        foreach (var debtor in debtors)
        {
            if (await _bills.LastReminderAtAsync(access.Group.Id, debtor, ct) is { } last && last + cooldown > now)
            {
                skipped.Add(new GroupRemindSkippedDto(debtor, last + cooldown));
                continue;
            }

            var reminder = new GroupDebtReminder { Id = Guid.NewGuid(), GroupId = access.Group.Id, DebtorUserId = debtor, SentByUserId = senderId, SentAt = now };
            _bills.AddReminder(reminder);
            reminders.Add(reminder);
            sent.Add(debtor);
        }

        if (reminders.Count > 0)
        {
            _groups.AddActivity(GroupService.NewActivity(access.Group.Id, senderId, GroupActivityType.DebtReminded, null, now));
            await _bills.SaveChangesAsync(ct);   // lưu trước khi gửi: lần nhắc đã tính dù push lỗi
            var sender = await _users.GetByIdAsync(senderId, ct);
            foreach (var reminder in reminders)
            {
                await NotifyAsync(access.Group, sender?.DisplayName ?? "Một thành viên", reminder, ct);
            }

            await _audit.LogAsync(AuditEvents.GroupDebtReminded, senderId, metadata: new { GroupId = access.Group.Id, Count = reminders.Count }, ct: ct);
        }

        return new GroupRemindResultDto(sent, skipped);
    }

    // Không bao giờ ném: lần nhắc đã được lưu. Không số tiền trong push.
    private async Task NotifyAsync(Group group, string senderName, GroupDebtReminder reminder, CancellationToken ct)
    {
        try
        {
            var member = await _groups.GetActiveMemberAsync(group.Id, reminder.DebtorUserId, ct);
            if (member is null || !member.AlertsEnabled)
            {
                return;
            }

            await _dispatcher.RaiseAsync(
                new ForecastAlertRequest(
                    reminder.DebtorUserId,
                    ForecastAlertKind.GroupDebtReminder,
                    $"group_debt_reminder:{group.Id}:{reminder.Id}",
                    "Nhắc thanh toán nợ nhóm",
                    $"{senderName} nhắc bạn thanh toán khoản nợ trong nhóm {group.Name}.",
                    new Dictionary<string, string> { ["groupId"] = group.Id.ToString() }),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group debt reminder push could not be sent for {GroupId}: {ExceptionType}", group.Id, ex.GetType().Name);
        }
    }

    private async Task<(Dictionary<Guid, long> Net, Dictionary<Guid, GroupMemberView> Members)> LoadAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await _bills.LoadDebtRowsAsync(groupId, ct);
        var members = (await _groups.ListActiveMembersAsync(groupId, ct)).ToDictionary(m => m.Member.UserId);
        return (GroupDebtMath.Net(rows.Bills, rows.Settlements), members);
    }
}
