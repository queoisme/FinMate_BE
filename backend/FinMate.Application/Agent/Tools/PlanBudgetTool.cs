using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Reports;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>plan_budget</c>: "thu X, muốn để dành Y mỗi tháng thì chi thế nào?". Phần còn lại (thu − tiết kiệm) chia theo tỉ lệ, ưu tiên:
/// chi tiêu của chính người dùng trong 3 tháng TRỌN gần nhất (giờ Việt Nam) → mẫu admin <c>agent.budgetTemplate</c> → <c>no_budget_basis</c>.
/// Mỗi nhóm làm tròn XUỐNG nghìn đồng; phần dư dồn vào nhóm có tỉ lệ lớn nhất (bằng nhau thì slug nhỏ nhất), nên tổng các nhóm + tiết kiệm = thu nhập ĐÚNG.
/// Chỉ giữ 7 nhóm có tỉ lệ lớn nhất; các nhóm còn lại gộp TRƯỚC khi chia thành một nhóm "Các nhóm khác" (<c>alloc_other</c>), để tổng bằng chứng ≤ 12 — đúng số mệnh đề mẫu dự phòng giữ lại.
/// Id nhóm theo vị trí (<c>alloc_1..7</c>, số tiền giảm dần), key <c>budget_alloc:&lt;slug&gt;</c> cắt cho vừa 64 ký tự.
/// Phép nhân trung gian dùng <see cref="Int128"/>: còn lại (≤ 1e11) × số tiền một nhóm (≤ 1e11) vượt <c>long</c>.
/// </summary>
public sealed class PlanBudgetTool(IFinancialDataSource source, ISystemConfigProvider config) : IAgentTool
{
    private const long MinVnd = 1_000;
    private const long MaxVnd = 100_000_000_000;
    private const long Rounding = 1_000;
    private const int IncomeHistoryMonths = 12;
    private const int ShareMonths = 3;
    private const int TopShares = 7;
    private const string OtherSlug = ""; // nhóm gộp; slug thật không bao giờ rỗng (mẫu admin rỗng bị coi là không hợp lệ)

    public ToolSpec Spec { get; } = new(
        "plan_budget",
        "Chia ngân sách tháng theo thu nhập (income_vnd, bỏ trống thì lấy trung vị thu nhập 12 tháng) và mục tiêu tiết kiệm save_vnd: phần còn lại chia theo tỉ lệ chi tiêu của người dùng hoặc mẫu của admin; số tiền tính bằng code, không phải LLM.",
        [
            new ToolArg("save_vnd", "int", true, MinVnd, MaxVnd),
            new ToolArg("income_vnd", "int", false, MinVnd, MaxVnd),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var save = args.GetProperty("save_vnd").GetInt64();
        long income;
        EvidenceSource incomeSource;
        if (args.TryGetProperty("income_vnd", out var given))
        {
            (income, incomeSource) = (given.GetInt64(), EvidenceSource.UserEntered);
        }
        else
        {
            var series = await source.GetMonthlySeriesAsync(context.UserId, IncomeHistoryMonths, context.Now, ct);
            (income, incomeSource) = (Statistics.Quantile(series.IncomeVnd.Order().ToArray(), 0.5), EvidenceSource.Estimated);
            if (income <= 0)
            {
                return ToolResult.Insufficient("history_too_short");
            }
        }

        var remaining = income - save;
        if (remaining <= 0)
        {
            return ToolResult.Insufficient("goal_exceeds_income");
        }

        var today = VietnamTime.DateOf(context.Now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var from = monthStart.AddMonths(-ShareMonths);
        var to = monthStart.AddDays(-1);
        var spend = (await source.GetCategorySpendAsync(context.UserId, from, to, ct)).Where(r => r.SpentVnd > 0).ToList();

        List<(string Slug, string Name, long Weight)> shares;
        string basis;
        if (spend.Count > 0)
        {
            shares = spend.Select(r => (r.Slug, r.Name, r.SpentVnd)).ToList();
            basis = $"theo tỉ lệ chi tiêu của bạn từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}";
        }
        else if (ParseTemplate((await config.GetAsync(ct)).Document.Agent.BudgetTemplate) is { } template)
        {
            shares = template.Select(t => (t.Slug, t.Slug, (long)t.Percent)).ToList(); // mẫu không có tên nhóm: dùng slug làm tên
            basis = "theo mẫu ngân sách của FinMate";
        }
        else
        {
            return ToolResult.Insufficient("no_budget_basis");
        }

        if (shares.Count > TopShares)
        {
            var ordered = shares.OrderByDescending(s => s.Weight).ThenBy(s => s.Slug, StringComparer.Ordinal).ToList();
            shares = [.. ordered.Take(TopShares), (OtherSlug, "Các nhóm khác", ordered.Skip(TopShares).Sum(s => s.Weight))];
        }

        var now = context.Now;
        Evidence Money(string id, string key, long value, EvidenceSource evidenceSource, string text)
            => new(id, key, value, text, "VND", evidenceSource, now, ConfidenceCalculator.Compute(evidenceSource, now, now, 1.0));

        var evidence = new List<Evidence>
        {
            Money("income", "income", income, incomeSource, incomeSource == EvidenceSource.UserEntered ? "Thu nhập tháng do người dùng nêu" : $"Thu nhập tháng điển hình (trung vị {IncomeHistoryMonths} tháng)"),
            Money("save", "save", save, EvidenceSource.UserEntered, "Số tiền muốn tiết kiệm mỗi tháng do người dùng nêu"),
            Money("remaining", "budget_remaining", remaining, incomeSource, "Phần còn lại để chi tiêu mỗi tháng (thu nhập − tiết kiệm)"),
        };

        var position = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (slug, name, amount) in Allocate(remaining, shares).OrderBy(b => b.Slug == OtherSlug))
        {
            var text = $"{name}: mức chi đề xuất mỗi tháng, {basis}";
            if (slug == OtherSlug)
            {
                evidence.Add(Money("alloc_other", "budget_alloc_other", amount, EvidenceSource.Estimated, text)); // key thật luôn có dấu hai chấm nên không trùng
                continue;
            }

            position++;
            var key = EvidenceKey.Bounded("budget_alloc:", slug);
            if (!keys.Add(key))
            {
                key = $"{key[..Math.Min(key.Length, EvidenceKey.MaxLength - 2)]}~{position}"; // hai slug dài trùng phần đầu: thêm vị trí (1..7) cho khác nhau
                keys.Add(key);
            }

            evidence.Add(Money($"alloc_{position}", key, amount, EvidenceSource.Estimated, text));
        }

        evidence.Add(new Evidence(
            "savings_rate", "savings_rate", null, $"Tiết kiệm chiếm {(save * 100 + income / 2) / income}% thu nhập", "pct",
            incomeSource, now, ConfidenceCalculator.Compute(incomeSource, now, now, 1.0)));

        return ToolResult.Ok(evidence);
    }

    /// <summary>Chia <paramref name="remaining"/> theo trọng số; trả các nhóm khác 0, số tiền giảm dần (bằng nhau thì theo slug), tổng đúng bằng <paramref name="remaining"/>.</summary>
    private static List<(string Slug, string Name, long Amount)> Allocate(long remaining, List<(string Slug, string Name, long Weight)> shares)
    {
        var total = (Int128)0;
        foreach (var s in shares)
        {
            total += s.Weight;
        }

        var buckets = shares
            .Select(s => (s.Slug, s.Name, s.Weight, Amount: (long)((Int128)remaining * s.Weight / total / Rounding * Rounding)))
            .OrderByDescending(b => b.Weight).ThenBy(b => b.Slug, StringComparer.Ordinal)
            .ToList();
        var largest = buckets[0];
        buckets[0] = largest with { Amount = largest.Amount + remaining - buckets.Sum(b => b.Amount) };

        return buckets.Where(b => b.Amount > 0)
            .OrderByDescending(b => b.Amount).ThenBy(b => b.Slug, StringComparer.Ordinal)
            .Select(b => (b.Slug, b.Name, b.Amount)).ToList();
    }

    /// <summary>Mẫu hợp lệ: object JSON, slug không rỗng và không trùng, phần trăm nguyên dương, tổng đúng 100. Sai bất kỳ điều gì thì null (coi như chưa có mẫu).</summary>
    private static List<(string Slug, int Percent)>? ParseTemplate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var items = new List<(string Slug, int Percent)>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(p.Name) || p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var percent) || percent <= 0)
                {
                    return null;
                }

                items.Add((p.Name, percent));
            }

            var unique = items.Select(i => i.Slug).Distinct(StringComparer.Ordinal).Count() == items.Count;
            return unique && items.Sum(i => (long)i.Percent) == 100 ? items : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
