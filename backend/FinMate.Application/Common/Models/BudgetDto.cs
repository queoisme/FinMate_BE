namespace FinMate.Application.Common.Models;

public record BudgetDto(
    Guid Id,
    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,
    long LimitCents,
    string PeriodType);

public record BudgetSummaryItemDto(
    Guid BudgetId,
    Guid? CategoryId,
    string? CategoryName,
    string? CategorySlug,
    long LimitCents,
    long SpentCents,
    long RemainingCents,
    int PercentUsed,
    bool IsOverLimit);

/// <summary>
/// <paramref name="TotalBudget"/> là hạn mức tổng (category_id NULL) nếu user có đặt, tách
/// riêng khỏi <paramref name="CategoryBudgets"/> thay vì gộp thành một con số "tổng": budget
/// tổng bao trùm mọi category nên cộng nó vào danh sách sẽ tính trùng mọi khoản chi.
/// PeriodStart/PeriodEnd trả về theo giờ VN (offset +07:00) cho đúng ranh giới người dùng thấy.
/// </summary>
public record BudgetSummaryDto(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    BudgetSummaryItemDto? TotalBudget,
    IReadOnlyList<BudgetSummaryItemDto> CategoryBudgets);
