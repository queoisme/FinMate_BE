using FinMate.Application.Common.Exceptions;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

public partial class GroupFundEntryService
{
    /// <summary>
    /// Hình dạng dòng (D3, D10, S4): chỉ chi mang danh mục và nơi chi, và chi phải có ít nhất một danh mục (trừ khi lấy từ giao dịch có sẵn);
    /// điều chỉnh không kèm ví; chỉ chi được tạo từ giao dịch có sẵn; không kèm cùng lúc ví và giao dịch nguồn. Sai → 422.
    /// </summary>
    private static void EnsureShape(
        GroupFundEntryKind kind, Guid? categoryId, Guid? groupCategoryId, string? merchantName, Guid? financialAccountId, Guid? fromTransactionId)
    {
        var isExpense = kind == GroupFundEntryKind.Expense;
        if (!isExpense && (categoryId is not null || groupCategoryId is not null || !string.IsNullOrWhiteSpace(merchantName)))
        {
            throw Invalid("Chỉ khoản chi từ quỹ mới có danh mục và nơi chi.");
        }

        if (isExpense && categoryId is null && groupCategoryId is null && fromTransactionId is null)
        {
            throw Invalid("Khoản chi từ quỹ phải có danh mục.");
        }

        if (financialAccountId is not null && !GroupFundMath.AcceptsWallet(kind))
        {
            throw Invalid("Điều chỉnh quỹ không kèm ví cá nhân.");
        }

        if (fromTransactionId is not null && (!isExpense || financialAccountId is not null))
        {
            throw Invalid("Chỉ khoản chi mới tạo được từ một giao dịch có sẵn, và khi đó không kèm thêm ví.");
        }
    }

    /// <summary>D10: danh mục là danh mục hệ thống (không phải cho vay/mượn), danh mục nhóm phải còn hoạt động trong nhóm này.</summary>
    private async Task EnsureCategoriesAsync(Guid groupId, GroupFundEntryKind kind, Guid? categoryId, Guid? groupCategoryId, CancellationToken ct)
    {
        if (kind != GroupFundEntryKind.Expense)
        {
            return;
        }

        if (categoryId is { } id)
        {
            var category = await _categories.GetByIdAsync(id, ct);
            if (category is null || !category.IsSystem || LoanExclusion.IsLoan(category) || category.Slug == IncomeSlug)
            {
                throw Invalid("Danh mục của khoản chi từ quỹ phải là danh mục chi tiêu chung của hệ thống.");
            }
        }

        if (groupCategoryId is { } groupCategory && await _finance.GetCategoryAsync(groupId, groupCategory, ct) is null)
        {
            throw new NotFoundException("GroupCategory", groupCategory);
        }
    }

    private async Task<GroupFund> RequireFundAsync(Guid groupId, Guid fundId, CancellationToken ct)
        => await _funds.GetAsync(groupId, fundId, ct) ?? throw new NotFoundException("GroupFund", fundId);

    /// <summary>Quỹ đã lưu trữ chỉ đọc (D16).</summary>
    private async Task<GroupFund> RequireWritableFundAsync(Guid groupId, Guid fundId, CancellationToken ct)
    {
        var fund = await RequireFundAsync(groupId, fundId, ct);
        return fund.IsArchived ? throw new BusinessRuleException(GroupErrorCodes.FundArchived, "Quỹ đã lưu trữ nên không ghi thêm được.") : fund;
    }

    private static BusinessRuleException Invalid(string message) => new(GroupErrorCodes.FundEntryInvalid, message);

    private const string IncomeSlug = "income";
}
