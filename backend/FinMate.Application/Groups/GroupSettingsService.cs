using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

/// <param name="AlertsEnabled">Cảnh báo của chính người xem (<c>PUT /groups/{id}/alerts</c>), để màn cài đặt đọc đủ một chỗ.</param>
public sealed record GroupSettingsDto(
    int? ClosingDay,
    DebtSettlementMethod DebtSettlementMethod,
    long? RequireApprovalAboveCents,
    bool NotifyOnNewTransaction,
    bool WeeklySummary,
    int WeeklySummaryDay,
    string WeeklySummaryTime,
    bool AlertsEnabled);

/// <summary>PUT thay toàn bộ: <c>ClosingDay</c> và <c>RequireApprovalAboveCents</c> null nghĩa là tắt (cuối tháng / không duyệt).</summary>
public sealed record UpdateGroupSettingsCommand(
    Guid UserId,
    Guid GroupId,
    int? ClosingDay,
    DebtSettlementMethod DebtSettlementMethod,
    long? RequireApprovalAboveCents,
    bool NotifyOnNewTransaction,
    bool WeeklySummary,
    int WeeklySummaryDay,
    string? WeeklySummaryTime);

public class UpdateGroupSettingsCommandValidator : AbstractValidator<UpdateGroupSettingsCommand>
{
    public UpdateGroupSettingsCommandValidator()
    {
        RuleFor(x => x.ClosingDay!.Value).InclusiveBetween(1, 28).WithMessage("Ngày chốt sổ phải từ 1 đến 28.").When(x => x.ClosingDay is not null);
        RuleFor(x => x.DebtSettlementMethod).IsInEnum().WithMessage("Cách cấn trừ nợ không hợp lệ.");
        RuleFor(x => x.RequireApprovalAboveCents!.Value).GreaterThan(0).WithMessage("Ngưỡng duyệt phải lớn hơn 0.").When(x => x.RequireApprovalAboveCents is not null);
        RuleFor(x => x.WeeklySummaryDay).InclusiveBetween(1, 7).WithMessage("Thứ gửi tóm tắt tuần phải từ 1 (thứ Hai) đến 7 (Chủ nhật).");
        RuleFor(x => x.WeeklySummaryTime).Must(t => t is null || GroupSettingsService.TryParseTime(t, out _)).WithMessage("Giờ gửi phải có dạng HH:mm.");
    }
}

public interface IGroupSettingsService
{
    Task<GroupSettingsDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupSettingsDto> UpdateAsync(UpdateGroupSettingsCommand command, CancellationToken ct = default);
}

public class GroupSettingsService : IGroupSettingsService
{
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IAuditLogService _audit;
    private readonly IValidator<UpdateGroupSettingsCommand> _validator;

    public GroupSettingsService(IGroupRepository groups, IGroupAccessService access, IAuditLogService audit, IValidator<UpdateGroupSettingsCommand> validator)
    {
        _groups = groups;
        _access = access;
        _audit = audit;
        _validator = validator;
    }

    public async Task<GroupSettingsDto> GetAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        return ToDto(access.Group, access.Member.AlertsEnabled);
    }

    public async Task<GroupSettingsDto> UpdateAsync(UpdateGroupSettingsCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var access = await _access.RequireOwnerAsync(command.GroupId, command.UserId, ct);
        var group = access.Group;
        TryParseTime(command.WeeklySummaryTime ?? "09:00", out var time);

        group.ClosingDay = command.ClosingDay;
        group.DebtSettlementMethod = command.DebtSettlementMethod;
        group.RequireApprovalAboveCents = command.RequireApprovalAboveCents;
        group.NotifyOnNewTransaction = command.NotifyOnNewTransaction;
        group.WeeklySummary = command.WeeklySummary;
        group.WeeklySummaryDay = command.WeeklySummaryDay;
        group.WeeklySummaryTime = time;
        var now = DateTimeOffset.UtcNow;
        group.UpdatedAt = now;

        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.GroupUpdated, null, now));
        await _groups.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.GroupUpdated, command.UserId, metadata: new { command.GroupId }, ct: ct);
        return ToDto(group, access.Member.AlertsEnabled);
    }

    internal static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);

    private static GroupSettingsDto ToDto(Domain.Entities.Group g, bool alertsEnabled)
        => new(g.ClosingDay, g.DebtSettlementMethod, g.RequireApprovalAboveCents, g.NotifyOnNewTransaction, g.WeeklySummary,
            g.WeeklySummaryDay, g.WeeklySummaryTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), alertsEnabled);
}
