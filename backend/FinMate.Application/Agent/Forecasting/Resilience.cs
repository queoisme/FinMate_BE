using FinMate.Application.Reports;

namespace FinMate.Application.Agent;

/// <param name="StressedIncomeVnd">Thu nhập an toàn sau khi giảm.</param>
/// <param name="MonthlyCapacityVnd">Thu nhập sau giảm trừ chi tiêu điển hình mỗi tháng (có thể âm).</param>
/// <param name="ProjectedBalanceVnd">Số dư sau <c>months</c> tháng (có thể âm).</param>
/// <param name="MonthsUntilEmpty">Số tháng nguyên số dư còn chịu được nếu mỗi tháng dư âm; null nếu mỗi tháng không âm.</param>
public sealed record StressResult(int IncomeDropPercent, long SafeIncomeVnd, long StressedIncomeVnd, long MonthlyCapacityVnd, long ProjectedBalanceVnd, int? MonthsUntilEmpty);

/// <param name="Driver"><c>price_growth</c>, <c>income</c> hoặc <c>spend</c>.</param>
/// <param name="MonthsIfDown">Số tháng đạt mục tiêu (kịch bản cơ sở) khi yếu tố GIẢM <c>deltaBps</c>; null = không đạt.</param>
/// <param name="MonthsIfUp">Như trên khi yếu tố TĂNG <c>deltaBps</c>.</param>
/// <param name="Swing">Chênh lệch số tháng giữa hai phía (không đạt tính là <see cref="AgentLimits.MaxSearchMonths"/> + 1): yếu tố nào lệch nhiều nhất là yếu tố quyết định nhất.</param>
public sealed record SensitivityRow(string Driver, int? MonthsIfDown, int? MonthsIfUp, int Swing);

/// <summary>Các phép tính chịu đựng (thuần hàm, số nguyên đồng): runway, bài kiểm tra giảm thu nhập, độ nhạy của kết luận mục tiêu với giả định.</summary>
public static class Resilience
{
    /// <summary>Số tháng NGUYÊN số dư đủ trang trải chi tiêu thiết yếu: <c>⌊số dư / chi tiêu⌋</c>. Số dư không dương thì 0. Chi tiêu phải dương (chia cho 0 không có nghĩa).</summary>
    public static int RunwayMonths(long balanceVnd, long essentialSpendVnd)
    {
        if (essentialSpendVnd <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(essentialSpendVnd), "Chi tiêu thiết yếu phải dương.");
        }

        return balanceVnd <= 0 ? 0 : (int)Math.Min(int.MaxValue, balanceVnd / essentialSpendVnd);
    }

    /// <summary>Thu nhập dùng để lập kế hoạch an toàn: nhóm không ổn định lấy Q10 (tháng thấp), nhóm ổn định lấy trung vị.</summary>
    public static long SafeIncome(IncomeProfile profile) => profile.Segment == IncomeSegment.Variable ? profile.Q10 : profile.Q50;

    public static StressResult StressTest(IncomeProfile profile, long monthlySpendVnd, long balanceVnd, int months, int incomeDropPercent)
    {
        if (incomeDropPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(incomeDropPercent), "Mức giảm phải từ 0 đến 100 phần trăm.");
        }

        var safe = SafeIncome(profile);
        var stressed = safe * (100 - incomeDropPercent) / 100;
        var capacity = stressed - monthlySpendVnd;
        var projected = balanceVnd + (capacity * months);
        int? untilEmpty = capacity >= 0 ? null : balanceVnd <= 0 ? 0 : (int)Math.Min(int.MaxValue, balanceVnd / -capacity);
        return new StressResult(incomeDropPercent, safe, stressed, capacity, projected, untilEmpty);
    }

    /// <summary>
    /// Mỗi yếu tố (tăng giá, thu nhập, chi tiêu) được tăng và giảm <paramref name="deltaBps"/> điểm cơ bản (100 = 1%; với tăng giá là 1%/năm, với thu nhập và chi tiêu là 1% giá trị), chạy lại
    /// kịch bản CƠ SỞ và xem số tháng đạt mục tiêu đổi bao nhiêu. Trả theo mức ảnh hưởng giảm dần (hòa thì giữ thứ tự tăng giá, thu nhập, chi tiêu).
    /// </summary>
    public static IReadOnlyList<SensitivityRow> Sensitivity(
        long priceVnd, long savingsVnd, IncomeProfile income, long spendQ10, long spendQ50, long spendQ90, int baseGrowthBps, int horizonMonths, int deltaBps)
    {
        int? BaseMonths(int growth, int incomeBps, int spendBps)
        {
            long Scale(long v, int bps) => v * (10_000 + bps) / 10_000;
            var scaled = income with { Q10 = Scale(income.Q10, incomeBps), Q50 = Scale(income.Q50, incomeBps), Q90 = Scale(income.Q90, incomeBps) };
            var simulation = GoalSimulator.Simulate(priceVnd, savingsVnd, scaled, Scale(spendQ10, spendBps), Scale(spendQ50, spendBps), Scale(spendQ90, spendBps), growth, horizonMonths);
            return simulation.Scenarios.Count == 0 ? null : simulation.Scenarios[1].MonthsToReach;
        }

        int Value(int? months) => months ?? AgentLimits.MaxSearchMonths + 1;
        SensitivityRow Row(string driver, int? down, int? up) => new(driver, down, up, Math.Abs(Value(up) - Value(down)));

        var rows = new[]
        {
            Row("price_growth", BaseMonths(baseGrowthBps - deltaBps, 0, 0), BaseMonths(baseGrowthBps + deltaBps, 0, 0)),
            Row("income", BaseMonths(baseGrowthBps, -deltaBps, 0), BaseMonths(baseGrowthBps, deltaBps, 0)),
            Row("spend", BaseMonths(baseGrowthBps, 0, -deltaBps), BaseMonths(baseGrowthBps, 0, deltaBps)),
        };
        return rows.Select((r, i) => (Row: r, Order: i)).OrderByDescending(x => x.Row.Swing).ThenBy(x => x.Order).Select(x => x.Row).ToList();
    }
}
