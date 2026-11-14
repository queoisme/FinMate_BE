using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups.Funds;

/// <summary>
/// Dòng của quỹ nhóm (Phase 85). Mọi lần ghi: đọc quỹ, tính, rồi lưu MỘT lần cùng ví cá nhân và kỳ ngân sách nhóm; push và cảnh báo chỉ gửi sau khi lưu.
/// Quỹ, ví và kỳ ngân sách đều có <c>xmin</c>; lớp <see cref="RetryingGroupFundEntryService"/> chạy lại khi hai người ghi cùng lúc.
/// </summary>
public interface IGroupFundEntryService
{
    Task<GroupFundEntryListResult> ListAsync(Guid groupId, Guid userId, GroupFundEntryQuery query, CancellationToken ct = default);
    Task<GroupFundEntryResult> CreateAsync(CreateGroupFundEntryCommand command, CancellationToken ct = default);
}

public partial class GroupFundEntryService : IGroupFundEntryService
{
    internal const int DefaultPageSize = 20;
    internal const int MaxPageSize = 200;

    private readonly IGroupFundRepository _funds;
    private readonly IGroupRepository _groups;
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupAccessService _access;
    private readonly IGroupLedger _ledger;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly IValidator<CreateGroupFundEntryCommand> _createValidator;
    private readonly IForecastAlertTrigger? _forecastAlerts;

    public GroupFundEntryService(
        IGroupFundRepository funds,
        IGroupRepository groups,
        IGroupFinanceRepository finance,
        IGroupAccessService access,
        IGroupLedger ledger,
        IFinancialAccountRepository accounts,
        ICategoryRepository categories,
        IValidator<CreateGroupFundEntryCommand> createValidator,
        IForecastAlertTrigger? forecastAlerts = null)
    {
        _funds = funds;
        _groups = groups;
        _finance = finance;
        _access = access;
        _ledger = ledger;
        _accounts = accounts;
        _categories = categories;
        _createValidator = createValidator;
        _forecastAlerts = forecastAlerts;
    }

    /// <summary>D15: mọi người có <c>ViewOverview</c> thấy sổ quỹ, không phụ thuộc <c>transactionVisibility</c>.</summary>
    public async Task<GroupFundEntryListResult> ListAsync(Guid groupId, Guid userId, GroupFundEntryQuery query, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        await RequireFundAsync(groupId, query.FundId, ct);
        var limit = query.Limit <= 0 ? DefaultPageSize : Math.Min(query.Limit, MaxPageSize);
        var page = await _funds.ListEntriesAsync(query with { Limit = limit }, ct);
        return new GroupFundEntryListResult(page.Items.Select(v => ToDto(v, userId)).ToList(), page.NextCursor);
    }

    public async Task<GroupFundEntryResult> CreateAsync(CreateGroupFundEntryCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        EnsureShape(command.Kind, command.CategoryId, command.GroupCategoryId, command.MerchantName, command.FinancialAccountId, command.FromTransactionId);
        await _access.RequireAsync(command.GroupId, command.UserId, PermissionFor(command.Kind), ct);
        var fund = await RequireWritableFundAsync(command.GroupId, command.FundId, ct);

        if (command.ClientRequestId is { } requestId
            && await _funds.GetEntryByClientRequestAsync(fund.Id, command.UserId, requestId, ct) is { } existing)
        {
            return await ResultAsync(fund, existing.Id, command.UserId, alreadyExisted: true, ct);
        }

        await EnsureCategoriesAsync(command.GroupId, command.Kind, command.CategoryId, command.GroupCategoryId, ct);
        var now = DateTimeOffset.UtcNow;
        var entry = NewEntry(command, now);
        if (command.FromTransactionId is { } sourceId)
        {
            await ConvertSourceAsync(entry, sourceId, command, now, ct);
        }

        GroupFundMath.Apply(fund, GroupFundMath.Signed(entry), now);
        _funds.AddEntry(entry);
        if (command.FinancialAccountId is { } accountId)
        {
            await LinkWalletAsync(entry, accountId, command.UserId, now, ct);
        }

        var alerts = await RecomputeGroupBudgetsAsync([entry], [GroupScopeTouch.Of(entry)], ct);
        AddActivity(entry, command.UserId, now);
        await _groups.SaveChangesAsync(ct);

        await AfterSaveAsync(command.UserId, alerts, ct);
        return await ResultAsync(fund, entry.Id, command.UserId, alreadyExisted: false, ct);
    }

    private static GroupFundEntry NewEntry(CreateGroupFundEntryCommand command, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        FundId = command.FundId,
        GroupId = command.GroupId,
        MemberUserId = command.UserId,
        Kind = command.Kind,
        AmountCents = command.AmountCents,
        TransactedAt = command.TransactedAt,
        CategoryId = command.CategoryId,
        GroupCategoryId = command.GroupCategoryId,
        Note = Trimmed(command.Note),
        MerchantName = Trimmed(command.MerchantName),
        ClientRequestId = command.ClientRequestId,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Giao dịch cá nhân gắn dòng (BR-FUND-07): ví phải là của người ghi; đổi số dư theo đúng vế của giao dịch.</summary>
    private async Task<Transaction> LinkWalletAsync(GroupFundEntry entry, Guid accountId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
        var transaction = GroupFundPersonalLink.Build(entry, account, now);
        GroupFundPersonalLink.ApplyBalance(transaction, account, +1, now);
        transaction.BalanceAfterCents = account.BalanceCents;
        _funds.AddPersonalTransaction(transaction);
        return transaction;
    }

    /// <summary>Chi từ quỹ tính vào ngân sách nhóm (BR-FUND-10): TÍNH LẠI các phạm vi bị chạm, cùng cách với giao dịch chia sẻ (D13).</summary>
    private Task<IReadOnlyList<GroupBudgetAlert>> RecomputeGroupBudgetsAsync(
        IReadOnlyCollection<GroupFundEntry> pending, IEnumerable<GroupScopeTouch?> touches, CancellationToken ct)
    {
        var touched = touches.OfType<GroupScopeTouch>().Distinct().ToList();
        return touched.Count == 0 ? Task.FromResult<IReadOnlyList<GroupBudgetAlert>>([]) : _ledger.RecomputeAsync([], pending, touched, ct);
    }

    private async Task AfterSaveAsync(Guid userId, IReadOnlyList<GroupBudgetAlert> alerts, CancellationToken ct)
    {
        await _ledger.SendAlertsAsync(alerts, ct);
        if (_forecastAlerts is not null)
        {
            await _forecastAlerts.UserChangedAsync(userId, ct);
        }
    }

    private void AddActivity(GroupFundEntry entry, Guid actorUserId, DateTimeOffset now)
    {
        var type = entry.Kind switch
        {
            GroupFundEntryKind.Contribution => GroupActivityType.FundContributed,
            GroupFundEntryKind.Withdrawal => GroupActivityType.FundWithdrawn,
            _ => (GroupActivityType?)null,   // D22: chi và điều chỉnh không ghi hoạt động
        };
        if (type is { } activity)
        {
            _groups.AddActivity(GroupService.NewActivity(entry.GroupId, actorUserId, activity, entry.Id, now));
        }
    }

    private async Task<GroupFundEntryResult> ResultAsync(GroupFund fund, Guid entryId, Guid viewerId, bool alreadyExisted, CancellationToken ct)
    {
        var view = await _funds.GetEntryViewAsync(fund.Id, entryId, ct) ?? throw new NotFoundException("GroupFundEntry", entryId);
        return new GroupFundEntryResult(ToDto(view, viewerId), GroupFundService.ToDto(fund), alreadyExisted);
    }

    internal static GroupFundEntryDto ToDto(GroupFundEntryView view, Guid viewerId)
    {
        var e = view.Entry;
        var name = view.MemberName is null ? GroupMemberService.FormerMemberName
            : view.IsMemberActive ? view.MemberName : GroupMemberService.InactiveMemberName;
        return new GroupFundEntryDto(
            e.Id, e.FundId, e.Kind, e.AmountCents, e.TransactedAt, e.MemberUserId, name, e.CategoryId, e.GroupCategoryId, view.CategoryName, e.Note,
            e.MerchantName, e.Origin, e.MemberUserId == viewerId ? view.HasPersonalLink : null, e.CreatedAt, e.UpdatedAt);
    }

    private static GroupPermission PermissionFor(GroupFundEntryKind kind)
        => kind == GroupFundEntryKind.Adjustment ? GroupPermission.ManageFunds : GroupPermission.RecordFundEntries;

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
