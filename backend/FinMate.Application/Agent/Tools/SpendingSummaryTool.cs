using System.Text.Json;
using FinMate.Application.Common;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>spending_summary</c>: "tháng này / N tháng qua tôi tiêu nhiều nhất vào đâu?". Khoảng ngày = từ ngày 1 của tháng cách đây <c>months-1</c> tháng đến HÔM NAY (giờ Việt Nam), nên tháng hiện tại được tính và còn dở.
/// Tổng = tổng các nhóm mà nguồn dữ liệu trả về (không truy vấn lần hai), nên luôn khớp: top 5 nhóm + "khác" = tổng. Phần trăm chỉ nằm trong <c>Text</c> (số nguyên, tính từ số long), không bao giờ là <c>ValueVnd</c>.
/// Chỉ trả số đã gom theo nhóm, không có ghi chú hay tên giao dịch.
/// </summary>
public sealed class SpendingSummaryTool(IFinancialDataSource source) : IAgentTool
{
    private const int TopCategories = 5;

    public ToolSpec Spec { get; } = new(
        "spending_summary",
        "Chi tiêu đã xác nhận theo nhóm trong months tháng gần nhất (tính cả tháng này, đến hôm nay): tổng chi và tối đa 5 nhóm chi nhiều nhất cùng phần trăm trên tổng.",
        [new ToolArg("months", "int", true, 1, 12)]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var months = (int)args.GetProperty("months").GetInt64();
        var today = VietnamTime.DateOf(context.Now);
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - months);

        var rows = await source.GetCategorySpendAsync(context.UserId, from, today, ct);
        var total = rows.Sum(r => r.SpentVnd);
        if (total <= 0)
        {
            return ToolResult.Insufficient("no_spending_history");
        }

        var now = context.Now;
        Evidence Money(string id, string key, long value, string text)
            => new(id, key, value, text, "VND", EvidenceSource.Confirmed, now, ConfidenceCalculator.Compute(EvidenceSource.Confirmed, now, now, 1.0));

        // Thứ tự xác định: số tiền giảm dần, hòa thì theo slug, để top 5 không đổi giữa các lần chạy.
        var ordered = rows.OrderByDescending(r => r.SpentVnd).ThenBy(r => r.Slug, StringComparer.Ordinal).ToList();
        var evidence = new List<Evidence>
        {
            Money("spend_total", "spend_total", total, $"Tổng chi từ {from:dd/MM/yyyy} đến hôm nay ({today:dd/MM/yyyy}), {months} tháng gần nhất tính cả tháng này"),
        };

        var number = 0;
        foreach (var row in ordered.Take(TopCategories))
        {
            evidence.Add(Money($"cat_{++number}", $"category_spend:{row.Slug}", row.SpentVnd, $"{row.Name}: {Percent(row.SpentVnd, total)}% tổng chi"));
        }

        var rest = ordered.Skip(TopCategories).Sum(r => r.SpentVnd);
        if (ordered.Count > TopCategories)
        {
            evidence.Add(Money("cat_other", "category_spend:other", rest, $"Các nhóm khác: {Percent(rest, total)}% tổng chi"));
        }

        return ToolResult.Ok(evidence);
    }

    /// <summary>Phần trăm nguyên, làm tròn nửa lên, hoàn toàn bằng số long.</summary>
    private static long Percent(long amount, long total) => (amount * 100 + total / 2) / total;
}
