using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups.Bills;

public sealed record GroupBillSplitInput(Guid UserId, long ShareCents);

public sealed record GroupBillSplitDto(Guid? UserId, string DisplayName, long ShareCents);

public sealed record GroupBillDto(
    Guid Id,
    Guid GroupId,
    string Title,
    long AmountCents,
    Guid? PaidByUserId,
    string PaidByName,
    DateTimeOffset OccurredAt,
    GroupBillSplitMode SplitMode,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? CategoryName,
    string? Note,
    int SplitCount,
    IReadOnlyList<GroupBillSplitDto> Splits,
    DateTimeOffset CreatedAt);

public sealed record GroupBillListResult(IReadOnlyList<GroupBillDto> Items, string? NextCursor);

/// <param name="Splits">Chia đều: chỉ cần <c>UserId</c> (<c>ShareCents</c> bị bỏ qua, server tự tính). Chia tùy chỉnh: tổng <c>ShareCents</c> phải bằng <c>AmountCents</c>.</param>
public sealed record CreateGroupBillCommand(
    Guid UserId,
    Guid GroupId,
    string Title,
    long AmountCents,
    Guid PaidByUserId,
    DateTimeOffset OccurredAt,
    GroupBillSplitMode SplitMode,
    IReadOnlyList<GroupBillSplitInput> Splits,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? Note);

public class CreateGroupBillCommandValidator : AbstractValidator<CreateGroupBillCommand>
{
    public const int TitleMaxLength = 200;
    public const int NoteMaxLength = 500;

    public CreateGroupBillCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Tên hóa đơn không được để trống.")
            .MaximumLength(TitleMaxLength).WithMessage($"Tên hóa đơn tối đa {TitleMaxLength} ký tự.");
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.SplitMode).IsInEnum().WithMessage("Cách chia không hợp lệ.");
        RuleFor(x => x.Note).MaximumLength(NoteMaxLength).WithMessage($"Ghi chú tối đa {NoteMaxLength} ký tự.");
        RuleFor(x => x.Splits).NotEmpty().WithMessage("Hóa đơn cần ít nhất một người chia.");
    }
}

public interface IGroupBillService
{
    Task<GroupBillDto> CreateAsync(CreateGroupBillCommand command, CancellationToken ct = default);
    Task<GroupBillListResult> ListAsync(Guid groupId, Guid userId, string? cursor, int limit, CancellationToken ct = default);
    Task<GroupBillDto> GetAsync(Guid groupId, Guid userId, Guid billId, CancellationToken ct = default);
}

public class GroupBillService : IGroupBillService
{
    private readonly IGroupBillRepository _bills;
    private readonly IGroupRepository _groups;
    private readonly IGroupFinanceRepository _finance;
    private readonly ICategoryRepository _categories;
    private readonly IGroupAccessService _access;
    private readonly IGroupLedger _ledger;
    private readonly ISystemConfigProvider _config;
    private readonly IAuditLogService _audit;
    private readonly IValidator<CreateGroupBillCommand> _validator;

    public GroupBillService(
        IGroupBillRepository bills,
        IGroupRepository groups,
        IGroupFinanceRepository finance,
        ICategoryRepository categories,
        IGroupAccessService access,
        IGroupLedger ledger,
        ISystemConfigProvider config,
        IAuditLogService audit,
        IValidator<CreateGroupBillCommand> validator)
    {
        _bills = bills;
        _groups = groups;
        _finance = finance;
        _categories = categories;
        _access = access;
        _ledger = ledger;
        _config = config;
        _audit = audit;
        _validator = validator;
    }

    public async Task<GroupBillDto> CreateAsync(CreateGroupBillCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ShareTransactions, ct);

        var members = (await _groups.ListActiveMembersAsync(command.GroupId, ct)).ToDictionary(m => m.Member.UserId);
        if (!members.ContainsKey(command.PaidByUserId))
        {
            throw Invalid(GroupErrorCodes.BillInvalid, "Người trả phải là thành viên đang hoạt động của nhóm.");
        }

        var participants = command.Splits.Select(s => s.UserId).ToList();
        if (participants.Distinct().Count() != participants.Count || participants.Any(p => !members.ContainsKey(p)))
        {
            throw Invalid(GroupErrorCodes.BillInvalid, "Người chia phải là thành viên đang hoạt động của nhóm và không được lặp.");
        }

        var shares = command.SplitMode == GroupBillSplitMode.Equal
            ? GroupDebtMath.EqualShares(command.AmountCents, participants, command.PaidByUserId)
            : command.Splits.Select(s => (s.UserId, s.ShareCents)).ToList();
        if (shares.Any(s => s.ShareCents <= 0) || shares.Sum(s => s.ShareCents) != command.AmountCents)
        {
            throw Invalid(GroupErrorCodes.BillSplitMismatch, "Tổng các phần chia phải bằng số tiền hóa đơn và mỗi phần phải lớn hơn 0.");
        }

        await EnsureCategoriesAsync(command, ct);

        var now = DateTimeOffset.UtcNow;
        var bill = new GroupBill
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            Title = command.Title.Trim(),
            AmountCents = command.AmountCents,
            PaidByUserId = command.PaidByUserId,
            OccurredAt = command.OccurredAt,
            SplitMode = command.SplitMode,
            CategoryId = command.CategoryId,
            GroupCategoryId = command.GroupCategoryId,
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            CreatedByUserId = command.UserId,
            CreatedAt = now,
            Splits = shares.Select(s => new GroupBillSplit { Id = Guid.NewGuid(), UserId = s.UserId, ShareCents = s.ShareCents }).ToList(),
        };

        // Đọc, tính, lưu một lần: hóa đơn đang ở bộ nhớ nên đưa thẳng vào tính lại ngân sách nhóm, cùng một SaveChanges.
        _bills.AddBill(bill);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.BillCreated, bill.Id, now));
        var alerts = await _ledger.RecomputeAsync([], [], [bill], [GroupScopeTouch.Of(bill)], ct);
        await _bills.SaveChangesAsync(ct);
        await _ledger.SendAlertsAsync(alerts, ct);
        await _audit.LogAsync(AuditEvents.GroupBillCreated, command.UserId, metadata: new { command.GroupId, BillId = bill.Id }, ct: ct);

        return ToDto(bill, members[command.PaidByUserId].DisplayName, null, NamesOf(members));
    }

    public async Task<GroupBillListResult> ListAsync(Guid groupId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var max = (await _config.GetAsync(ct)).Document.Groups.MaxBillsPerPage;
        var seeAll = access.Has(GroupPermission.ViewMemberTransactions);
        var page = await _bills.ListAsync(groupId, seeAll ? null : userId, cursor, GroupPaging.Clamp(limit, max), ct);
        var names = NamesOf((await _groups.ListActiveMembersAsync(groupId, ct)).ToDictionary(m => m.Member.UserId));
        return new GroupBillListResult(page.Items.Select(v => ToDto(v.Bill, v.PaidByName, v, names)).ToList(), page.NextCursor);
    }

    public async Task<GroupBillDto> GetAsync(Guid groupId, Guid userId, Guid billId, CancellationToken ct = default)
    {
        var access = await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        var view = await _bills.GetViewAsync(groupId, billId, ct) ?? throw new NotFoundException("GroupBill", billId);
        var seeAll = access.Has(GroupPermission.ViewMemberTransactions);
        if (!seeAll && view.Bill.PaidByUserId != userId && view.Bill.Splits.All(s => s.UserId != userId))
        {
            throw new NotFoundException("GroupBill", billId);   // như khoản của người khác trong chế độ Summary: không tiết lộ là có
        }

        var names = NamesOf((await _groups.ListActiveMembersAsync(groupId, ct)).ToDictionary(m => m.Member.UserId));
        return ToDto(view.Bill, view.PaidByName, view, names);
    }

    private async Task EnsureCategoriesAsync(CreateGroupBillCommand command, CancellationToken ct)
    {
        if (command.CategoryId is { } id)
        {
            var category = await _categories.GetByIdAsync(id, ct);
            if (category is null || !category.IsSystem || LoanExclusion.IsLoan(category) || category.Slug == "income")
            {
                throw Invalid(GroupErrorCodes.BillInvalid, "Danh mục của hóa đơn phải là danh mục chi tiêu chung của hệ thống.");
            }
        }

        if (command.GroupCategoryId is { } groupCategory && await _finance.GetCategoryAsync(command.GroupId, groupCategory, ct) is null)
        {
            throw new NotFoundException("GroupCategory", groupCategory);
        }
    }

    private static Dictionary<Guid, string> NamesOf(Dictionary<Guid, GroupMemberView> members)
        => members.ToDictionary(p => p.Key, p => p.Value.IsUserActive ? p.Value.DisplayName : GroupMemberService.InactiveMemberName);

    private static GroupBillDto ToDto(GroupBill bill, string? paidByName, GroupBillView? view, IReadOnlyDictionary<Guid, string> names)
        => new(
            bill.Id, bill.GroupId, bill.Title, bill.AmountCents, bill.PaidByUserId,
            paidByName ?? (bill.PaidByUserId is { } p && names.TryGetValue(p, out var n) ? n : "Thành viên cũ"),
            bill.OccurredAt, bill.SplitMode, bill.CategoryId, bill.GroupCategoryId, view?.GroupCategoryName ?? view?.CategoryName, bill.Note, bill.Splits.Count,
            bill.Splits.Select(s => new GroupBillSplitDto(s.UserId, s.UserId is { } u && names.TryGetValue(u, out var name) ? name : "Thành viên cũ", s.ShareCents)).ToList(),
            bill.CreatedAt);

    private static BusinessRuleException Invalid(string code, string message) => new(code, message);
}
