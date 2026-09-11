using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Reports;

internal static class InsightMapper
{
    internal static SpendingInsightDto ToDto(SpendingInsight insight) => new(
        insight.Id,
        ToSlug(insight.InsightType),
        insight.Title,
        insight.Body,
        insight.CategoryId,
        insight.Category?.Name,
        insight.AmountCents,
        insight.PeriodStart,
        insight.PeriodEnd,
        insight.IsRead,
        insight.CreatedAt);

    /// <summary>Trả đúng chuỗi snake_case như lưu trong DB, không phải tên enum PascalCase.</summary>
    internal static string ToSlug(Domain.Enums.InsightType type) => type switch
    {
        Domain.Enums.InsightType.VsLastMonth => "vs_last_month",
        Domain.Enums.InsightType.RecurringDetected => "recurring_detected",
        Domain.Enums.InsightType.UnusualSpending => "unusual_spending",
        _ => type.ToString().ToLowerInvariant(),
    };
}
