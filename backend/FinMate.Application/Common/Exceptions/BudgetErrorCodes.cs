namespace FinMate.Application.Common.Exceptions;

public static class BudgetErrorCodes
{
    public const string AlreadyExists = "BUDGET_CATEGORY_ALREADY_EXISTS";

    /// <summary>Chuyển hạn mức nhiều hơn phần chưa chi của ngân sách nguồn trong kỳ hiện tại.</summary>
    public const string TransferExceedsUnspent = "BUDGET_TRANSFER_EXCEEDS_UNSPENT";

    /// <summary>Chuyển hạn mức từ/vào ngân sách tổng: ngân sách tổng bao trùm mọi danh mục nên chuyển qua nó không có nghĩa.</summary>
    public const string TransferNeedsCategoryBudgets = "BUDGET_TRANSFER_NEEDS_CATEGORY_BUDGETS";
}
