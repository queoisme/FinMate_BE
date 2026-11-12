using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>simulate_purchase</c> (Flow 6): "mua món giá X hôm nay / N ngày nữa có ổn không?". KHÔNG tự tính dòng tiền: bọc <see cref="IPurchaseCheckService"/> (engine dự báo dòng tiền theo ngày của Phase 54–55,
/// dùng khoản định kỳ <c>recurring_items</c>, ngân sách và kế hoạch tháng) rồi đổi kết quả thành bằng chứng có nguồn và độ tin cậy để agent trích dẫn. Một nguồn tính duy nhất: số trong câu trả lời của agent
/// bằng đúng số mà màn "Mua món này có ổn không?" trong app hiển thị. Kết luận (khớp <c>AgentAnswerDto.Verdict</c>): <b>infeasible</b> = có nguy cơ thiếu tiền; <b>tight</b> = cần cân nhắc hoặc lệch kế hoạch;
/// <b>feasible</b> = phù hợp kế hoạch. Đọc dữ liệu thật của người dùng đang hỏi (<c>userId</c> từ JWT), nên tab thử của admin (hồ sơ giả lập) không chạy được công cụ này.
/// </summary>
public sealed class SimulatePurchaseTool(IPurchaseCheckService purchases) : IAgentTool
{
    private const int MaxEvidenceText = 480;

    public ToolSpec Spec { get; } = new(
        "simulate_purchase",
        "Mô phỏng một khoản mua giá price_vnd, sau purchase_in_days ngày nữa (mặc định hôm nay): số dư thấp nhất trong kỳ tính trước và sau khi mua, theo từng khoản thu/chi định kỳ.",
        [
            new ToolArg("price_vnd", "int", true, 1, AgentLimits.MaxPriceVnd),
            new ToolArg("purchase_in_days", "int", false, 0, AgentLimits.MaxSimulationDays),
            new ToolArg("window_days", "int", false, 1, AgentLimits.MaxSimulationDays),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var price = args.GetProperty("price_vnd").GetInt64();
        var inDays = (int)Optional(args, "purchase_in_days", 0);
        int? horizon = args.TryGetProperty("window_days", out var w) && w.TryGetInt64(out var days) ? (int)days : null;
        var date = VietnamTime.DateOf(context.Now).AddDays(inDays);

        var check = await purchases.CheckAsync(new PurchaseCheckCommand(context.UserId, price, date, null, null, null, horizon), ct);
        if (check.Status == PurchaseStatuses.InsufficientData || check.LowestAfter is null || check.LowestBefore is null)
        {
            return ToolResult.Insufficient("purchase_data_missing");
        }

        var now = context.Now;
        var asOf = check.BalanceAsOf ?? now;
        var estimated = check.Status != PurchaseStatuses.Assessable || check.Lines.Any(l => l.Source == PurchaseSources.Estimated) || check.Confidence == "low";
        var derived = estimated ? EvidenceSource.Estimated : EvidenceSource.Confirmed;

        Evidence Money(string id, string key, long value, EvidenceSource source, string text)
            => new(id, key, value, Cap(text), "VND", source, asOf, ConfidenceCalculator.Compute(source, asOf, now, 1.0));

        var period = $"{check.Period.From:dd/MM}–{check.Period.To:dd/MM}";
        var evidence = new List<Evidence>
        {
            Money("price", "purchase_price", price, EvidenceSource.UserEntered, $"Giá khoản mua do người dùng nêu, mua ngày {check.PurchaseDate:dd/MM}"),
            Money("min_before", "min_balance_before", check.LowestBefore.BalanceCents, derived, $"Số dư thấp nhất kỳ {period} nếu KHÔNG mua (ngày {check.LowestBefore.Day:dd/MM})"),
            Money("min_after", "min_balance_after", check.LowestAfter.BalanceCents, derived, $"Số dư thấp nhất kỳ {period} nếu mua (ngày {check.LowestAfter.Day:dd/MM})"),
            Money("buffer", "safety_buffer", check.BufferCents, EvidenceSource.Estimated, "Vùng đệm an toàn theo cấu hình hệ thống"),
        };
        if (check.ConservativeLowestAfterCents is { } conservative)
        {
            evidence.Add(Money("min_after_conservative", "min_balance_after_conservative", conservative, EvidenceSource.Estimated, "Số dư thấp nhất sau khi mua theo đường thận trọng (chi hằng ngày cao)"));
        }

        if (check.ShortfallCents > 0)
        {
            evidence.Add(Money("shortfall", "shortfall", check.ShortfallCents, derived, "Số tiền dự kiến thiếu so với mức an toàn"));
        }

        if (check.MaxAffordableCents is { } affordable)
        {
            evidence.Add(Money("max_affordable", "max_affordable", affordable, EvidenceSource.Estimated, "Mức tối đa có thể mua ngày đó mà vẫn giữ vùng đệm"));
        }

        var number = 0;
        foreach (var line in check.Lines)
        {
            var source = line.Source switch
            {
                PurchaseSources.Confirmed => EvidenceSource.Confirmed,
                PurchaseSources.UserEntered => EvidenceSource.UserEntered,
                _ => EvidenceSource.Estimated,
            };
            var when = line.Date is { } d ? $" ngày {d:dd/MM}" : string.Empty;
            evidence.Add(Money($"scheduled_{++number}", line.AmountCents >= 0 ? "scheduled_inflow" : "scheduled_obligation", Math.Abs(line.AmountCents), source, $"{line.Label}{when} ({(line.AmountCents >= 0 ? "thu" : "chi")})"));
        }

        if (check.EarliestSafeDate is { } safe)
        {
            evidence.Add(new Evidence("earliest_safe_date", "earliest_safe_date", null, $"Ngày sớm nhất mua mà vẫn an toàn: {safe:dd/MM/yyyy}", "date", EvidenceSource.Estimated, asOf, ConfidenceCalculator.Compute(EvidenceSource.Estimated, asOf, now, 1.0)));
        }

        foreach (var warning in check.Warnings.Take(3))
        {
            evidence.Add(new Evidence($"warning_{warning.Code}", $"warning_{warning.Code}", null, Cap(warning.Message), "text", EvidenceSource.Estimated, asOf, ConfidenceCalculator.Compute(EvidenceSource.Estimated, asOf, now, 1.0)));
        }

        var verdict = check.Verdict switch
        {
            PurchaseVerdicts.ShortfallRisk => "infeasible",
            PurchaseVerdicts.FitsPlan => estimated ? "tight" : "feasible",
            _ => "tight",
        };
        evidence.Add(new Evidence("verdict", "purchase_verdict", null, Cap($"verdict: {verdict} (kỳ tính {period}; {check.Summary})"), "label", derived, asOf, ConfidenceCalculator.Compute(derived, asOf, now, 1.0)));

        // Chỉ đề xuất khi khoản mua chưa ổn: có lý do để hoãn hoặc giảm. Mỗi thẻ trỏ tới bằng chứng cùng tên ở trên.
        var actions = new List<SuggestedActionDto>();
        if (verdict != "feasible")
        {
            if (check.EarliestSafeDate is { } safeDate && safeDate > check.PurchaseDate)
            {
                actions.Add(new SuggestedActionDto(SuggestedActionKinds.WaitUntil, $"Hoãn mua tới {safeDate:dd/MM/yyyy}", null, safeDate.ToString("yyyy-MM-dd"), null, null, ["earliest_safe_date"]));
            }

            if (check.MaxAffordableCents is { } cap && cap > 0 && cap < price)
            {
                actions.Add(new SuggestedActionDto(SuggestedActionKinds.LowerAmountTo, $"Giảm số tiền mua xuống tối đa {LocalTemplateComposer.FormatVnd(cap)}", cap, null, null, null, ["max_affordable"]));
            }
        }

        return ToolResult.Ok(evidence, actions);
    }

    private static string Cap(string text) => text.Length <= MaxEvidenceText ? text : text[..MaxEvidenceText];

    private static long Optional(JsonElement args, string name, long fallback)
        => args.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : fallback;
}
