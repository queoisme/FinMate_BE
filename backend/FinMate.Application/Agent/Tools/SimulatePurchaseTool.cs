using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Domain.Entities;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>simulate_purchase</c> (Flow 6): "mua món giá X hôm nay / N ngày nữa có ổn không?". Dựng sổ dòng tiền THEO NGÀY từ tổng số dư và các khoản định kỳ, rồi so điểm thấp nhất của số dư
/// trong kỳ tính TRƯỚC và SAU khi mua. Kỳ tính: từ hôm nay đến ngay trước khoản thu định kỳ đã xác nhận kế tiếp; không có thì <c>window_days</c> (mặc định 30) ngày. Kết luận (khớp
/// <c>AgentAnswerDto.Verdict</c>): <b>infeasible</b> = có ngày số dư dưới 0 sau khi mua (kèm ngày và số thiếu); <b>tight</b> = không âm nhưng kết quả dựa vào khoản CHƯA xác nhận hoặc chưa có khoản định kỳ
/// nào được xác nhận; <b>feasible</b> = không âm và mọi khoản đưa vào đều đã xác nhận. Không bao giờ nói "chắc chắn". Khoản đã có giao dịch thanh toán trong tháng không bị trừ hai lần.
/// </summary>
public sealed class SimulatePurchaseTool(IFinancialDataSource source) : IAgentTool
{
    public ToolSpec Spec { get; } = new(
        "simulate_purchase",
        "Mô phỏng một khoản mua giá price_vnd, sau purchase_in_days ngày nữa (mặc định hôm nay): số dư thấp nhất trong kỳ tính trước và sau khi mua, theo từng khoản định kỳ.",
        [
            new ToolArg("price_vnd", "int", true, 1, AgentLimits.MaxPriceVnd),
            new ToolArg("purchase_in_days", "int", false, 0, AgentLimits.MaxSimulationDays),
            new ToolArg("reserve_vnd", "int", false, 0, AgentLimits.MaxPriceVnd),
            new ToolArg("window_days", "int", false, 1, AgentLimits.MaxSimulationDays),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var price = args.GetProperty("price_vnd").GetInt64();
        var inDays = Optional(args, "purchase_in_days", 0);
        var reserve = Optional(args, "reserve_vnd", 0);
        var windowDays = (int)Optional(args, "window_days", AgentLimits.DefaultSimulationDays);

        var today = VietnamTime.DateOf(context.Now);
        var balance = await source.GetTotalBalanceAsync(context.UserId, ct);
        var entries = await source.GetRecurringAsync(context.UserId, context.Now, ct);

        var occurrences = entries.SelectMany(e => Occurrences(e, today, today.AddDays(AgentLimits.MaxSimulationDays))).ToList();
        var nextConfirmedInflow = occurrences
            .Where(o => o.Entry.Kind == RecurringKind.Inflow && o.Entry.Source == EvidenceSource.Confirmed && o.Item.Date > today && !o.Item.Settled)
            .Select(o => o.Item.Date)
            .DefaultIfEmpty()
            .Min();
        var end = nextConfirmedInflow == default ? today.AddDays(windowDays) : nextConfirmedInflow.AddDays(-1);
        end = end < today ? today : end;

        var inflows = occurrences.Where(o => o.Entry.Kind == RecurringKind.Inflow).Select(o => o.Item).ToList();
        var obligations = occurrences.Where(o => o.Entry.Kind == RecurringKind.Obligation).Select(o => o.Item).ToList();
        var purchase = new HypotheticalPurchase(price, today.AddDays((int)inDays));
        var before = CashFlowLedger.Project(balance, inflows, obligations, reserve, null, today, end);
        var after = CashFlowLedger.Project(balance, inflows, obligations, reserve, purchase, today, end);

        var used = occurrences.Where(o => !o.Item.Settled && o.Item.Date >= today && o.Item.Date <= end).ToList();
        var anyEstimated = used.Any(o => o.Entry.Source == EvidenceSource.Estimated);
        var noConfirmedItems = !entries.Any(e => e.Source == EvidenceSource.Confirmed);
        var derivedSource = anyEstimated || noConfirmedItems ? EvidenceSource.Estimated : EvidenceSource.Confirmed;

        var now = context.Now;
        Evidence Money(string id, string key, long value, EvidenceSource evidenceSource, string text)
            => new(id, key, value, text, "VND", evidenceSource, now, ConfidenceCalculator.Compute(evidenceSource, now, now, 1.0));

        var evidence = new List<Evidence>
        {
            Money("price", "purchase_price", price, EvidenceSource.UserEntered, $"Giá khoản mua do người dùng nêu, mua sau {inDays} ngày"),
            Money("balance", "total_balance", balance, EvidenceSource.Confirmed, "Tổng số dư các tài khoản hôm nay"),
        };
        if (reserve > 0)
        {
            evidence.Add(Money("reserve", "living_reserve", reserve, EvidenceSource.UserEntered, "Khoản dự trữ sinh hoạt người dùng muốn giữ lại trong kỳ"));
        }

        var number = 0;
        foreach (var o in used.OrderBy(o => o.Item.Date))
        {
            var label = o.Entry.Kind == RecurringKind.Inflow ? "thu" : "trả";
            var origin = o.Entry.Source == EvidenceSource.Confirmed ? "đã xác nhận" : "chưa xác nhận (ước tính từ lịch sử)";
            evidence.Add(Money($"scheduled_{++number}", o.Entry.Kind == RecurringKind.Inflow ? "scheduled_inflow" : "scheduled_obligation", o.Item.AmountVnd, o.Entry.Source, $"{o.Entry.Name}: {label} ngày {o.Item.Date:dd/MM}, {origin}"));
        }

        evidence.Add(Money("min_before", "min_balance_before", before.MinimumBalanceVnd, derivedSource, $"Số dư thấp nhất từ {today:dd/MM} đến {end:dd/MM} nếu KHÔNG mua (ngày {before.MinimumDate:dd/MM})"));
        evidence.Add(Money("min_after", "min_balance_after", after.MinimumBalanceVnd, derivedSource, $"Số dư thấp nhất từ {today:dd/MM} đến {end:dd/MM} nếu mua (ngày {after.MinimumDate:dd/MM})"));
        if (after.MinimumBalanceVnd < 0)
        {
            evidence.Add(Money("shortfall", "shortfall", -after.MinimumBalanceVnd, derivedSource, $"Số tiền dự kiến thiếu vào ngày {after.MinimumDate:dd/MM}"));
        }

        var verdict = after.MinimumBalanceVnd < 0 ? "infeasible" : derivedSource == EvidenceSource.Estimated ? "tight" : "feasible";
        evidence.Add(new Evidence(
            "verdict", "purchase_verdict", null,
            $"verdict: {verdict} (kỳ tính {today:dd/MM}–{end:dd/MM}; {(noConfirmedItems ? "chưa có khoản định kỳ nào được xác nhận" : anyEstimated ? "có khoản chưa xác nhận" : "mọi khoản đã xác nhận")})",
            "label", derivedSource, now, ConfidenceCalculator.Compute(derivedSource, now, now, 1.0)));

        return ToolResult.Ok(evidence);
    }

    private static long Optional(JsonElement args, string name, long fallback)
        => args.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : fallback;

    private sealed record Occurrence(RecurringEntry Entry, ScheduledItem Item);

    /// <summary>Các lần xảy ra của một khoản định kỳ trong [from, to]. Lần của THÁNG HIỆN TẠI mang cờ đã thanh toán nếu đã có giao dịch thật.</summary>
    private static IEnumerable<Occurrence> Occurrences(RecurringEntry entry, DateOnly from, DateOnly to)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var date = new DateOnly(month.Year, month.Month, Math.Min(entry.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
            if (date < from || date > to)
            {
                continue;
            }

            var settled = entry.PaidThisMonth && month.Year == from.Year && month.Month == from.Month;
            yield return new Occurrence(entry, new ScheduledItem($"{entry.Name}:{date:yyyy-MM-dd}", entry.Name, entry.AmountVnd, date, entry.Source, settled));
        }
    }
}
