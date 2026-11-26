using System.Text.Json;
using FinMate.Application.Common;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>compare_spending</c>: "tháng này tôi chi nhiều hơn tháng trước không?", "so với quý trước thì sao?". So <c>months</c> tháng TRỌN gần nhất (không gồm tháng đang dở, vì so một tháng dở với một tháng trọn
/// luôn ra "giảm") với <c>months</c> tháng trọn ngay trước đó. Bằng chứng: tổng hai kỳ, chênh lệch tuyệt đối (chữ nói rõ tăng, giảm hay không đổi), tối đa 3 nhóm đổi nhiều nhất.
/// Phần trăm chỉ nằm trong <c>Text</c> (số nguyên, tính từ số long) và chỉ khi kỳ trước > 0 (không chia cho 0); nhóm mới xuất hiện hoặc không còn chi thì nói bằng chữ, không có phần trăm.
/// Chỉ trả số đã gom theo nhóm, không có ghi chú hay tên giao dịch.
/// </summary>
public sealed class CompareSpendingTool(IFinancialDataSource source) : IAgentTool
{
    private const int TopChanges = 3;

    public ToolSpec Spec { get; } = new(
        "compare_spending",
        "So chi tiêu đã xác nhận của months tháng trọn gần nhất (không gồm tháng đang dở) với months tháng trọn ngay trước đó: tổng hai kỳ, chênh lệch tăng hay giảm và tối đa 3 nhóm đổi nhiều nhất.",
        [new ToolArg("months", "int", true, 1, 6)]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var months = (int)args.GetProperty("months").GetInt64();
        var today = VietnamTime.DateOf(context.Now);
        var currentFrom = new DateOnly(today.Year, today.Month, 1).AddMonths(-months);
        var currentTo = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var previousFrom = currentFrom.AddMonths(-months);
        var previousTo = currentFrom.AddDays(-1);

        var current = await source.GetCategorySpendAsync(context.UserId, currentFrom, currentTo, ct);
        var previous = await source.GetCategorySpendAsync(context.UserId, previousFrom, previousTo, ct);
        long currentTotal = current.Sum(r => r.SpentVnd), previousTotal = previous.Sum(r => r.SpentVnd);
        if (currentTotal <= 0 && previousTotal <= 0)
        {
            return ToolResult.Insufficient("no_spending_history");
        }

        if (previousTotal <= 0)
        {
            return ToolResult.Insufficient("no_previous_period");
        }

        var now = context.Now;
        Evidence Money(string id, string key, long value, string text)
            => new(id, key, value, text, "VND", EvidenceSource.Confirmed, now, ConfidenceCalculator.Compute(EvidenceSource.Confirmed, now, now, 1.0));

        var change = currentTotal - previousTotal;
        var evidence = new List<Evidence>
        {
            Money("period_current", "period_current", currentTotal, $"Tổng chi {months} tháng trọn gần nhất ({currentFrom:dd/MM/yyyy} đến {currentTo:dd/MM/yyyy})"),
            Money("period_previous", "period_previous", previousTotal, $"Tổng chi {months} tháng trọn ngay trước đó ({previousFrom:dd/MM/yyyy} đến {previousTo:dd/MM/yyyy})"),
            Money("period_change", "period_change", Math.Abs(change), Describe("Tổng chi", change, previousTotal)),
        };

        var previousBySlug = previous.ToDictionary(r => r.Slug, r => r.SpentVnd, StringComparer.Ordinal);
        var currentBySlug = current.ToDictionary(r => r.Slug, r => r.SpentVnd, StringComparer.Ordinal);
        var names = current.Concat(previous).GroupBy(r => r.Slug, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var changes = names.Keys
            .Select(slug => (Slug: slug, Name: names[slug], Now: currentBySlug.GetValueOrDefault(slug), Before: previousBySlug.GetValueOrDefault(slug)))
            .Where(c => c.Now != c.Before)
            .OrderByDescending(c => Math.Abs(c.Now - c.Before)).ThenBy(c => c.Slug, StringComparer.Ordinal)
            .Take(TopChanges);

        var number = 0;
        foreach (var c in changes)
        {
            evidence.Add(Money($"chg_{++number}", EvidenceKey.Bounded("category_change:", c.Slug), Math.Abs(c.Now - c.Before), Describe(c.Name, c.Now - c.Before, c.Before, c.Before == 0, c.Now == 0)));
        }

        return ToolResult.Ok(evidence);
    }

    /// <summary>Câu mô tả một thay đổi. Phần trăm nguyên (làm tròn nửa lên) chỉ khi kỳ trước > 0; mới xuất hiện / không còn chi thì nói bằng chữ.</summary>
    private static string Describe(string label, long change, long before, bool appeared = false, bool gone = false)
    {
        if (change == 0)
        {
            return $"{label}: không đổi";
        }

        if (appeared)
        {
            return $"{label}: mới xuất hiện trong kỳ này";
        }

        if (gone)
        {
            return $"{label}: không còn chi trong kỳ này";
        }

        var percent = (Math.Abs(change) * 100 + before / 2) / before;
        return $"{label}: {(change > 0 ? "tăng" : "giảm")} {percent}% so với kỳ trước";
    }
}
