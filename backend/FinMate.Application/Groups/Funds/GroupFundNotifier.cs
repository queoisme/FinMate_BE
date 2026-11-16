using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// Push của quỹ nhóm qua hộp thư <c>forecast_alerts</c> (chống trùng, lưu trước khi gửi). Không bao giờ ném: tiền đã lưu rồi, báo lỗi lúc này
/// khiến client gửi lại và ghi trùng. Không số tiền; không log tên quỹ. Tắt <c>features.groups</c> hoặc <c>features.groupFunds</c> thì không gửi.
/// </summary>
public interface IGroupFundNotifier
{
    /// <summary>S3: người quản lý xoá dòng của <paramref name="entry"/>.MemberUserId; báo cho đúng người đó.</summary>
    Task EntryRemovedAsync(GroupFund fund, GroupFundEntry entry, CancellationToken ct = default);

    /// <summary>
    /// BR-FUND-16, D18: quỹ vừa chuyển từ ≥ 0 sang âm. Báo mỗi người đang có <c>ManageFunds</c> và bật cảnh báo nhóm, một lần cho mỗi đợt âm: khoá chống lặp
    /// có <see cref="GroupFund.NegativeEpisode"/>, nên về ≥ 0 rồi âm lại thì được báo lại.
    /// </summary>
    Task FundWentNegativeAsync(GroupFund fund, CancellationToken ct = default);
}

public class GroupFundNotifier : IGroupFundNotifier
{
    internal const string DataGroupId = "groupId";
    internal const string DataFundId = "fundId";
    internal const string DataEntryId = "entryId";

    private readonly IGroupRepository _groups;
    private readonly IForecastAlertDispatcher _dispatcher;
    private readonly ISystemConfigProvider _config;
    private readonly ILogger<GroupFundNotifier> _logger;

    public GroupFundNotifier(IGroupRepository groups, IForecastAlertDispatcher dispatcher, ISystemConfigProvider config, ILogger<GroupFundNotifier> logger)
    {
        _groups = groups;
        _dispatcher = dispatcher;
        _config = config;
        _logger = logger;
    }

    public async Task EntryRemovedAsync(GroupFund fund, GroupFundEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (entry.MemberUserId is not { } ownerId || !await IsEnabledAsync(ct))
            {
                return;
            }

            var member = await _groups.GetActiveMemberAsync(fund.GroupId, ownerId, ct);
            if (member is null || !member.AlertsEnabled)
            {
                return;
            }

            var data = new Dictionary<string, string>
            {
                [DataGroupId] = fund.GroupId.ToString(),
                [DataFundId] = fund.Id.ToString(),
                [DataEntryId] = entry.Id.ToString(),
            };
            await _dispatcher.RaiseAsync(
                new ForecastAlertRequest(
                    ownerId,
                    ForecastAlertKind.GroupFundRemoved,
                    $"group_fund_removed:{entry.Id}",
                    "Một dòng quỹ của bạn đã bị xoá",
                    $"Người quản lý đã xoá một dòng của bạn trong quỹ {fund.Name}. Giao dịch trong sổ cá nhân của bạn vẫn còn, bạn có thể tự xoá nếu muốn.",
                    data),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group fund push could not be sent for {FundId}: {ExceptionType}", fund.Id, ex.GetType().Name);
        }
    }

    public async Task FundWentNegativeAsync(GroupFund fund, CancellationToken ct = default)
    {
        try
        {
            var group = await _groups.GetByIdAsync(fund.GroupId, ct);
            if (group is null || !await IsEnabledAsync(ct))
            {
                return;
            }

            var data = new Dictionary<string, string> { [DataGroupId] = fund.GroupId.ToString(), [DataFundId] = fund.Id.ToString() };
            foreach (var view in await _groups.ListActiveMembersAsync(fund.GroupId, ct))
            {
                var member = view.Member;
                var manages = GroupPermissions.Effective(member.Role, group.TransactionVisibility, member.PermissionsGranted, member.PermissionsRevoked)
                    .HasFlag(GroupPermission.ManageFunds);
                if (view.IsUserActive && member.AlertsEnabled && manages)
                {
                    await _dispatcher.RaiseAsync(
                        new ForecastAlertRequest(
                            member.UserId,
                            ForecastAlertKind.GroupFundNegative,
                            $"group_fund_negative:{fund.Id}:{fund.NegativeEpisode}",
                            "Quỹ nhóm đang âm",
                            $"Quỹ {fund.Name} đang âm: có người đang ứng trước cho quỹ.",
                            data),
                        ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Group fund push could not be sent for {FundId}: {ExceptionType}", fund.Id, ex.GetType().Name);
        }
    }

    private async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var features = (await _config.GetAsync(ct)).Document.Features;
        return features.Groups && features.GroupFunds;
    }
}
