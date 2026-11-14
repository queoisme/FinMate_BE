using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

public partial class GroupFundEntryService
{
    public async Task<GroupFundEntryResult> UpdateAsync(UpdateGroupFundEntryCommand command, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(command.GroupId, command.UserId, ct);
        var fund = await RequireWritableFundAsync(command.GroupId, command.FundId, ct);
        var entry = await RequireLiveEntryAsync(fund.Id, command.EntryId, ct);
        EnsureMayUpdate(access, entry, command);
        EnsureUpdateShape(entry, command);
        await EnsureCategoriesAsync(command.GroupId, entry.Kind, command.CategoryId, command.GroupCategoryId, ct);

        // Chỉ chủ dòng có sổ cá nhân gắn với nó; khoá dòng giao dịch trước (thứ tự: giao dịch → quỹ → ví → kỳ ngân sách).
        var personal = entry.MemberUserId == command.UserId
            ? await _funds.GetPersonalTransactionAsync(entry.Id, command.UserId, includeDeleted: true, ct)
            : null;
        var alerts = personal is null
            ? await ApplyUpdateAsync(fund, entry, null, command, ct)
            : await _transactions.RunWithTransactionsLockedAsync(
                command.UserId, [personal.Id], rows => ApplyUpdateAsync(fund, entry, rows[personal.Id], command, ct), ct);

        await AfterSaveAsync(command.UserId, alerts, ct);
        return await ResultAsync(fund, entry.Id, command.UserId, alreadyExisted: false, ct);
    }

    public async Task<GroupFundDto> DeleteAsync(Guid groupId, Guid userId, Guid fundId, Guid entryId, CancellationToken ct = default)
    {
        var access = await _access.RequireMemberAsync(groupId, userId, ct);
        var fund = await RequireWritableFundAsync(groupId, fundId, ct);
        var entry = await RequireLiveEntryAsync(fund.Id, entryId, ct);
        var isOwner = entry.MemberUserId == userId;
        if (!access.Has(isOwner ? PermissionFor(entry.Kind) : GroupPermission.ManageFunds))
        {
            throw new ForbiddenException("Bạn không có quyền xoá dòng quỹ này.");
        }

        var personal = isOwner ? await _funds.GetPersonalTransactionAsync(entry.Id, userId, ct: ct) : null;
        var (alerts, effects) = personal is null
            ? await ApplyDeleteAsync(fund, entry, null, userId, isOwner, ct)
            : await _transactions.RunWithTransactionsLockedAsync(userId, [personal.Id], rows => ApplyDeleteAsync(fund, entry, rows[personal.Id], userId, isOwner, ct), ct);

        await AfterSaveAsync(userId, alerts, ct);
        if (effects is not null)
        {
            await _converter.AfterSaveAsync(userId, effects, ct);
        }

        if (!isOwner)
        {
            await _audit.LogAsync(AuditEvents.GroupFundEntryDeleted, userId, metadata: new { GroupId = groupId, FundId = fundId, EntryId = entryId }, ct: ct);
            await _notifier.EntryRemovedAsync(fund, entry, ct);
        }

        return GroupFundService.ToDto(fund);
    }

    private async Task<IReadOnlyList<GroupBudgetAlert>> ApplyUpdateAsync(
        GroupFund fund, GroupFundEntry entry, Transaction? personal, UpdateGroupFundEntryCommand command, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var before = GroupScopeTouch.Of(entry);
        var signedBefore = GroupFundMath.Signed(entry);

        entry.AmountCents = command.AmountCents ?? entry.AmountCents;
        entry.TransactedAt = command.TransactedAt ?? entry.TransactedAt;
        entry.CategoryId = command.CategoryId ?? entry.CategoryId;
        entry.GroupCategoryId = command.GroupCategoryId ?? entry.GroupCategoryId;
        entry.Note = command.Note is null ? entry.Note : Trimmed(command.Note);
        entry.MerchantName = command.MerchantName is null ? entry.MerchantName : Trimmed(command.MerchantName);
        entry.UpdatedAt = now;

        GroupFundMath.Apply(fund, GroupFundMath.Signed(entry) - signedBefore, now);
        await SyncPersonalAsync(entry, personal, command, now, ct);
        var alerts = await RecomputeGroupBudgetsAsync([entry], [before, GroupScopeTouch.Of(entry)], ct);
        await _groups.SaveChangesAsync(ct);
        return alerts;
    }

    private async Task<(IReadOnlyList<GroupBudgetAlert> Alerts, Transactions.Refunds.RefundEffects? Effects)> ApplyDeleteAsync(
        GroupFund fund, GroupFundEntry entry, Transaction? personal, Guid userId, bool isOwner, CancellationToken ct)
    {
        Transactions.Refunds.RefundEffects? effects = null;
        var now = DateTimeOffset.UtcNow;
        var touch = GroupScopeTouch.Of(entry);
        GroupFundMath.Apply(fund, -GroupFundMath.Signed(entry), now);
        entry.DeletedAt = now;
        entry.DeletedByUserId = userId;
        entry.UpdatedAt = now;

        // D24: khoản chi lấy từ giao dịch ngân hàng về lại khoản chi cá nhân; khoản tạo mới thì bị xoá mềm và hoàn ví (BR-FUND-09).
        if (personal is { DeletedAt: null } && entry.Origin == GroupFundEntryOrigin.Converted)
        {
            effects = await _converter.RestoreAsync(userId, personal, entry, ct);
        }
        else if (personal is { DeletedAt: null })
        {
            await RemovePersonalAsync(entry, personal, userId, now, ct);
        }

        if (!isOwner)
        {
            _groups.AddActivity(GroupService.NewActivity(entry.GroupId, userId, GroupActivityType.FundEntryDeleted, entry.Id, now));
        }

        var alerts = await RecomputeGroupBudgetsAsync([entry], [touch], ct);
        await _groups.SaveChangesAsync(ct);
        return (alerts, effects);
    }

    /// <summary>
    /// Giữ giao dịch cá nhân khớp với dòng: bỏ ví thì xoá mềm và hoàn ví; đổi hoặc gắn lại ví thì dùng lại đúng dòng giao dịch đó (mỗi dòng quỹ chỉ
    /// có một), hoàn vế cũ rồi áp vế mới. Không đổi ví thì chỉ cập nhật số tiền, thời điểm, ghi chú.
    /// </summary>
    private async Task SyncPersonalAsync(GroupFundEntry entry, Transaction? personal, UpdateGroupFundEntryCommand command, DateTimeOffset now, CancellationToken ct)
    {
        if (entry.Origin == GroupFundEntryOrigin.Converted)
        {
            return;   // D24: giao dịch ngân hàng giữ nguyên số tiền, ngày, ví và mô tả của nó
        }

        var live = personal is { DeletedAt: null } ? personal : null;
        if (command.RemoveFinancialAccount)
        {
            if (live is not null)
            {
                await RemovePersonalAsync(entry, live, command.UserId, now, ct);
            }

            return;
        }

        var targetAccountId = command.FinancialAccountId ?? live?.FinancialAccountId;
        if (targetAccountId is not { } accountId)
        {
            return;
        }

        if (personal is null)
        {
            await LinkWalletAsync(entry, accountId, command.UserId, now, ct);
            return;
        }

        if (live is not null)
        {
            await ApplyPersonalBalanceAsync(live, command.UserId, -1, now, ct);
        }

        personal.DeletedAt = null;
        personal.FinancialAccountId = accountId;
        personal.AmountCents = entry.AmountCents;
        personal.TransactedAt = entry.TransactedAt;
        personal.Description = entry.Note;
        personal.UpdatedAt = now;
        personal.BalanceAfterCents = await ApplyPersonalBalanceAsync(personal, command.UserId, +1, now, ct);
    }

    /// <summary>Gỡ giao dịch cá nhân của chủ dòng: hoàn ví, xoá mềm, bỏ nghi trùng đang trỏ tới nó (BR-DUP-08).</summary>
    private async Task RemovePersonalAsync(GroupFundEntry entry, Transaction personal, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await ApplyPersonalBalanceAsync(personal, userId, -1, now, ct);
        personal.DeletedAt = now;
        personal.UpdatedAt = now;
        foreach (var linked in await _transactions.ListDuplicateLinksToAsync(userId, personal.Id, ct))
        {
            if (linked.DuplicateStatus == DuplicateStatus.Suspected)
            {
                linked.DuplicateOfTransactionId = null;
                linked.DuplicateStatus = null;
                linked.UpdatedAt = now;
            }
        }
    }

    /// <returns>Số dư ví sau khi áp.</returns>
    private async Task<long> ApplyPersonalBalanceAsync(Transaction personal, Guid userId, int sign, DateTimeOffset now, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(personal.FinancialAccountId, userId, ct)
            ?? throw new NotFoundException("FinancialAccount", personal.FinancialAccountId);
        GroupFundPersonalLink.ApplyBalance(personal, account, sign, now);
        return account.BalanceCents;
    }

    /// <summary>D5: chủ dòng sửa được mọi trường (với quyền ghi loại đó); người quản lý chỉ sửa danh mục và ghi chú dòng của người khác.</summary>
    private static void EnsureMayUpdate(GroupAccess access, GroupFundEntry entry, UpdateGroupFundEntryCommand command)
    {
        var touchesOwnerFields = command.AmountCents is not null || command.TransactedAt is not null || command.MerchantName is not null
            || command.FinancialAccountId is not null || command.RemoveFinancialAccount;
        var allowed = entry.MemberUserId == command.UserId
            ? access.Has(PermissionFor(entry.Kind))
            : access.Has(GroupPermission.ManageFunds) && !touchesOwnerFields;
        if (!allowed)
        {
            throw new ForbiddenException("Bạn không sửa được dòng quỹ này. Không ai sửa được số tiền dòng của người khác.");
        }

        if (entry.Origin == GroupFundEntryOrigin.Converted
            && (command.AmountCents is not null || command.TransactedAt is not null || command.FinancialAccountId is not null || command.RemoveFinancialAccount))
        {
            throw new BusinessRuleException(GroupErrorCodes.FundEntryLocked, "Khoản chi lấy từ giao dịch ngân hàng giữ nguyên số tiền, thời điểm và ví.");
        }
    }

    private static void EnsureUpdateShape(GroupFundEntry entry, UpdateGroupFundEntryCommand command)
    {
        var isAdjustment = entry.Kind == GroupFundEntryKind.Adjustment;
        if (command.AmountCents is { } amount && (isAdjustment ? amount == 0 : amount <= 0))
        {
            throw ValidationFailed(nameof(command.AmountCents), isAdjustment ? "Số tiền điều chỉnh phải khác 0." : "Số tiền phải lớn hơn 0.");
        }

        if (isAdjustment && command.Note is not null && string.IsNullOrWhiteSpace(command.Note))
        {
            throw ValidationFailed(nameof(command.Note), "Điều chỉnh quỹ phải có ghi chú.");
        }

        if (command.Note?.Length > CreateGroupFundEntryCommandValidator.NoteMaxLength
            || command.MerchantName?.Length > CreateGroupFundEntryCommandValidator.MerchantMaxLength)
        {
            throw ValidationFailed(nameof(command.Note), "Ghi chú hoặc nơi chi quá dài.");
        }

        if (command.RemoveFinancialAccount && command.FinancialAccountId is not null)
        {
            throw Invalid("Không thể vừa bỏ ví vừa chọn ví.");
        }

        if (entry.Kind != GroupFundEntryKind.Expense && (command.CategoryId is not null || command.GroupCategoryId is not null || command.MerchantName is not null))
        {
            throw Invalid("Chỉ khoản chi từ quỹ mới có danh mục và nơi chi.");
        }

        if (command.FinancialAccountId is not null && !GroupFundMath.AcceptsWallet(entry.Kind))
        {
            throw Invalid("Điều chỉnh quỹ không kèm ví cá nhân.");
        }
    }

    private static FluentValidation.ValidationException ValidationFailed(string field, string message)
        => new([new FluentValidation.Results.ValidationFailure(field, message)]);

    private async Task<GroupFundEntry> RequireLiveEntryAsync(Guid fundId, Guid entryId, CancellationToken ct)
    {
        var entry = await _funds.GetEntryAsync(fundId, entryId, ct);
        return entry is { DeletedAt: null } ? entry : throw new NotFoundException("GroupFundEntry", entryId);
    }
}
