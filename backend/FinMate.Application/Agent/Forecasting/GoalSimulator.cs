namespace FinMate.Application.Agent;

public enum GoalVerdict
{
    Feasible,
    Tight,
    Infeasible,
    InsufficientData,
}

/// <param name="Name"><c>conservative</c>, <c>base</c> hoặc <c>optimistic</c>.</param>
/// <param name="MonthlyCapacityVnd">Thu trừ chi mỗi tháng của kịch bản (có thể âm).</param>
/// <param name="MonthsToReach">Số tháng đạt mục tiêu; null nếu không đạt trong <see cref="AgentLimits.MaxSearchMonths"/> tháng (hoặc dư âm).</param>
/// <param name="ShortfallVnd">Còn thiếu bao nhiêu tại cuối horizon (0 nếu đã đủ).</param>
public sealed record ScenarioResult(string Name, long MonthlyCapacityVnd, int? MonthsToReach, long ShortfallVnd);

public sealed record GoalSimulation(GoalVerdict Verdict, IReadOnlyList<ScenarioResult> Scenarios, long PriceAtHorizonVnd);

/// <summary>
/// "Bao lâu nữa đủ tiền mua X" bằng số nguyên đồng. Ba kịch bản lấy các phân vị đối nghịch của thu và chi: bảo thủ = thu Q10 − chi Q90, cơ sở = Q50 − Q50, lạc quan = thu Q90 − chi Q10.
/// Giá mục tiêu tăng theo tháng bằng số nguyên: <c>price = price × (10000 + annualBps/12) / 10000</c>, chia nguyên (cắt phần dư) — tiền không đi qua <c>double</c>.
/// </summary>
public static class GoalSimulator
{
    public static GoalSimulation Simulate(
        long priceVnd, long currentSavingsVnd, IncomeProfile income,
        long monthlySpendQ10, long monthlySpendQ50, long monthlySpendQ90,
        int priceGrowthBpsPerYear, int horizonMonths)
    {
        if (income.MonthsOfHistory < AgentLimits.MinHistoryMonths)
        {
            return new GoalSimulation(GoalVerdict.InsufficientData, [], priceVnd);
        }

        var monthlyBps = priceGrowthBpsPerYear / 12;
        var scenarios = new[]
        {
            Scenario("conservative", income.Q10 - monthlySpendQ90),
            Scenario("base", income.Q50 - monthlySpendQ50),
            Scenario("optimistic", income.Q90 - monthlySpendQ10),
        };

        var verdict = scenarios[0].MonthsToReach <= horizonMonths ? GoalVerdict.Feasible
            : scenarios[1].MonthsToReach <= horizonMonths ? GoalVerdict.Tight
            : GoalVerdict.Infeasible;

        return new GoalSimulation(verdict, scenarios, PriceAfter(priceVnd, monthlyBps, horizonMonths));

        ScenarioResult Scenario(string name, long capacity)
        {
            int? months = null;
            var price = priceVnd;
            for (var m = 0; m <= AgentLimits.MaxSearchMonths; m++)
            {
                if (m > 0)
                {
                    price = price * (10_000 + monthlyBps) / 10_000;
                }

                if (currentSavingsVnd + (capacity * m) >= price)
                {
                    months = m;
                    break;
                }
            }

            var priceAtHorizon = PriceAfter(priceVnd, monthlyBps, horizonMonths);
            var shortfall = Math.Max(0, priceAtHorizon - (currentSavingsVnd + (capacity * horizonMonths)));
            return new ScenarioResult(name, capacity, months, shortfall);
        }
    }

    private static long PriceAfter(long price, int monthlyBps, int months)
    {
        for (var i = 0; i < months; i++)
        {
            price = price * (10_000 + monthlyBps) / 10_000;
        }

        return price;
    }
}
