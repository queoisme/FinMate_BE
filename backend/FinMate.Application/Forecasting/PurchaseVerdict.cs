namespace FinMate.Application.Forecasting;

public static class PurchaseStatuses
{
    public const string Assessable = "assessable";
    public const string Partial = "partial";
    public const string InsufficientData = "insufficient_data";
}

public static class PurchaseVerdicts
{
    public const string ShortfallRisk = "shortfall_risk";
    public const string Consider = "consider";
    public const string OffPlan = "off_plan";
    public const string FitsPlan = "fits_plan";
    public const string InsufficientData = "insufficient_data";
}

public static class PurchasePeriodBases
{
    public const string NextIncome = "next_income";
    public const string UserChosen = "user_chosen";
    public const string DefaultHorizon = "default_horizon";
}

public static class PurchaseSources
{
    public const string Confirmed = "confirmed";
    public const string UserEntered = "user_entered";
    public const string Estimated = "estimated";
}

public static class PurchaseImpactStatuses
{
    public const string Ok = "ok";
    public const string Over = "over";
    public const string Unknown = "unknown";
}

public static class PurchaseWarningCodes
{
    public const string NoAccount = "no_account";
    public const string LowConfidence = "low_confidence";
    public const string NoIncomeSchedule = "no_income_schedule";
    public const string StaleBalance = "stale_balance";
    public const string AlreadyShort = "already_short";
    public const string LowMargin = "low_margin";
    public const string BudgetOver = "budget_over";
    public const string BudgetUnknown = "budget_unknown";
    public const string PlanOver = "plan_over";
    public const string PlanUnknown = "plan_unknown";
}

public sealed record PurchasePointDto(DateOnly Day, long BalanceCents);

public sealed record PurchasePeriodDto(DateOnly From, DateOnly To, string Basis);

public sealed record PurchaseLineDto(DateOnly? Date, string Label, long AmountCents, string Source);

public sealed record PurchaseWarningDto(string Code, string Message);

/// <summary>Tác động lên ngân sách danh mục. <paramref name="Status"/> là <c>unknown</c> khi chưa có danh mục/ngân sách tương ứng: không coi ngân sách bằng 0.</summary>
public sealed record PurchaseBudgetImpactDto(string Status, string? Name, long? LimitCents, long? ProjectedCents, long? ProjectedAfterCents);

/// <summary>Tác động lên kế hoạch tháng (<c>/me/spendable</c>): thu nhập tháng − đã chi − tiền dành cho mục tiêu.</summary>
public sealed record PurchasePlanImpactDto(string Status, long? SpendableCents, long? SpendableAfterCents);

/// <param name="BeforeCents">Số dư dự kiến nếu không mua.</param>
/// <param name="AfterCents">Số dư dự kiến nếu mua.</param>
/// <param name="AfterLowCents">Đường thận trọng sau khi mua (chi hằng ngày theo phân vị 75).</param>
public sealed record PurchaseDay(DateOnly Date, long BeforeCents, long AfterCents, long AfterLowCents);

public sealed record PurchaseQuality(
    bool HasAccount,
    bool LowConfidence,
    bool IncomeScheduleKnown,
    bool HorizonChosenByUser,
    int? BalanceAgeDays,
    int StaleBalanceDays);

public sealed record PurchaseVerdictInput(
    DateOnly Today,
    DateOnly PurchaseDate,
    long AmountCents,
    long BufferCents,
    PurchasePeriodDto Period,
    IReadOnlyList<PurchaseDay> Days,
    IReadOnlyList<DateOnly> IncomeDates,
    int HorizonDays,
    PurchaseQuality Quality,
    PurchaseBudgetImpactDto Budget,
    PurchasePlanImpactDto Plan);

public sealed record PurchaseVerdictResult(
    string Status,
    string Verdict,
    PurchasePointDto? LowestBefore,
    PurchasePointDto? LowestAfter,
    long? ConservativeLowestAfterCents,
    long ShortfallCents,
    long? MaxAffordableCents,
    DateOnly? EarliestSafeDate,
    IReadOnlyList<PurchaseWarningDto> Warnings);

/// <summary>
/// Kết luận "mua khoản này có ổn không" từ bốn kiểm tra độc lập: chất lượng dữ liệu, thanh khoản theo ngày, ngân sách, kế hoạch tháng.
/// Hàm thuần: cùng input luôn ra cùng kết quả, không đụng dữ liệu thật. Đây là đánh giá khả năng chịu khoản chi trong một kỳ, không phải
/// phán xét món hàng có đáng mua hay không; vùng đệm là số người dùng tự đặt nên không có phần trăm tùy ý ở đây.
/// </summary>
public static class PurchaseVerdict
{
    /// <summary>
    /// Kỳ tính: tới ngày trước kỳ thu nhập kế tiếp SAU ngày mua. Chưa biết kỳ đó thì dùng mốc người dùng chọn, không có nữa thì mốc mặc định;
    /// không bao giờ tự suy ra ngày lương.
    /// </summary>
    public static PurchasePeriodDto ResolvePeriod(
        DateOnly today, DateOnly purchaseDate, IReadOnlyList<DateOnly> incomeDates, int? requestedHorizonDays, int defaultHorizonDays, DateOnly lastDay)
    {
        var upcoming = incomeDates.Where(d => d > purchaseDate).ToList();
        if (upcoming.Count > 0)
        {
            return new PurchasePeriodDto(today, upcoming.Min().AddDays(-1), PurchasePeriodBases.NextIncome);
        }

        var end = purchaseDate.AddDays(requestedHorizonDays ?? defaultHorizonDays);
        var basis = requestedHorizonDays is null ? PurchasePeriodBases.DefaultHorizon : PurchasePeriodBases.UserChosen;
        return new PurchasePeriodDto(today, end > lastDay ? lastDay : end, basis);
    }

    public static PurchaseVerdictResult Evaluate(PurchaseVerdictInput input)
    {
        var warnings = new List<PurchaseWarningDto>();
        var status = StatusOf(input, warnings);
        if (status == PurchaseStatuses.InsufficientData)
        {
            return new PurchaseVerdictResult(status, PurchaseVerdicts.InsufficientData, null, null, null, 0, null, null, warnings);
        }

        var period = input.Days.Where(d => d.Date <= input.Period.To).ToList();
        var before = period.MinBy(d => d.BeforeCents)!;
        var after = period.MinBy(d => d.AfterCents)!;
        var conservative = period.Min(d => d.AfterLowCents);
        var buffer = Math.Max(0, input.BufferCents);

        AddLiquidityWarnings(warnings, before, after, conservative);
        AddImpactWarnings(warnings, input);

        var verdict = VerdictOf(status, after.AfterCents, buffer, input);
        var needsAlternatives = verdict is PurchaseVerdicts.ShortfallRisk or PurchaseVerdicts.Consider;
        return new PurchaseVerdictResult(
            status,
            verdict,
            new PurchasePointDto(before.Date, before.BeforeCents),
            new PurchasePointDto(after.Date, after.AfterCents),
            conservative,
            Math.Max(0, -after.AfterCents),
            MaxAffordable(input, buffer),
            needsAlternatives ? EarliestSafeDate(input, buffer) : null,
            warnings);
    }

    private static string StatusOf(PurchaseVerdictInput input, List<PurchaseWarningDto> warnings)
    {
        var quality = input.Quality;
        if (!quality.HasAccount)
        {
            warnings.Add(new PurchaseWarningDto(PurchaseWarningCodes.NoAccount, "Chưa có ví nào được tính vào dự báo nên chưa đánh giá được."));
            return PurchaseStatuses.InsufficientData;
        }

        var partial = false;
        if (quality.LowConfidence)
        {
            warnings.Add(new PurchaseWarningDto(PurchaseWarningCodes.LowConfidence, "Dữ liệu chi tiêu còn ít nên mức chi hằng ngày chỉ là ước tính thô."));
            partial = true;
        }

        if (!quality.IncomeScheduleKnown && !quality.HorizonChosenByUser)
        {
            warnings.Add(new PurchaseWarningDto(
                PurchaseWarningCodes.NoIncomeSchedule,
                $"Chưa biết ngày nhận thu nhập kế tiếp nên chỉ đánh giá trong {input.HorizonDays} ngày sau ngày mua. Hãy thêm khoản thu định kỳ để chính xác hơn."));
            partial = true;
        }

        if (quality.BalanceAgeDays is { } age && age > quality.StaleBalanceDays)
        {
            warnings.Add(new PurchaseWarningDto(PurchaseWarningCodes.StaleBalance, $"Số dư được cập nhật cách đây {age} ngày; hãy cập nhật số dư để kết quả đúng hơn."));
            partial = true;
        }

        return partial ? PurchaseStatuses.Partial : PurchaseStatuses.Assessable;
    }

    private static void AddLiquidityWarnings(List<PurchaseWarningDto> warnings, PurchaseDay before, PurchaseDay after, long conservative)
    {
        if (before.BeforeCents < 0)
        {
            warnings.Add(new PurchaseWarningDto(
                PurchaseWarningCodes.AlreadyShort,
                $"Dù chưa mua, dự báo đã thiếu tiền vào {ForecastText.Day(before.Date)} (còn {ForecastText.Money(before.BeforeCents)})."));
        }

        if (conservative < 0 && after.AfterCents >= 0)
        {
            warnings.Add(new PurchaseWarningDto(
                PurchaseWarningCodes.LowMargin,
                $"Nếu chi tiêu hằng ngày cao hơn thường lệ, số dư có thể xuống {ForecastText.Money(conservative)}."));
        }
    }

    private static void AddImpactWarnings(List<PurchaseWarningDto> warnings, PurchaseVerdictInput input)
    {
        var budget = input.Budget;
        if (budget.Status == PurchaseImpactStatuses.Over)
        {
            warnings.Add(new PurchaseWarningDto(
                PurchaseWarningCodes.BudgetOver,
                $"Khoản này làm ngân sách {budget.Name ?? "tổng"} vượt hạn mức {ForecastText.Money(budget.LimitCents ?? 0)} (dự kiến {ForecastText.Money(budget.ProjectedAfterCents ?? 0)})."));
        }
        else if (budget.Status == PurchaseImpactStatuses.Unknown)
        {
            warnings.Add(new PurchaseWarningDto(PurchaseWarningCodes.BudgetUnknown, "Chưa rõ tác động lên ngân sách (chưa chọn danh mục hoặc chưa có ngân sách tương ứng)."));
        }

        var plan = input.Plan;
        if (plan.Status == PurchaseImpactStatuses.Over)
        {
            warnings.Add(new PurchaseWarningDto(
                PurchaseWarningCodes.PlanOver,
                $"Khoản này làm số còn lại trong kế hoạch tháng xuống {ForecastText.Money(plan.SpendableAfterCents ?? 0)}."));
        }
        else if (plan.Status == PurchaseImpactStatuses.Unknown)
        {
            warnings.Add(new PurchaseWarningDto(PurchaseWarningCodes.PlanUnknown, "Chưa rõ tác động lên kế hoạch tháng (chưa khai thu nhập hoặc ngày mua ở tháng khác)."));
        }
    }

    /// <summary>
    /// Thiếu tiền luôn được báo, kể cả khi dữ liệu chỉ ở mức một phần (nói "có nguy cơ" thì an toàn). Ngược lại "phù hợp" chỉ khi dữ liệu đủ tin cậy.
    /// </summary>
    private static string VerdictOf(string status, long lowestAfter, long buffer, PurchaseVerdictInput input)
    {
        if (lowestAfter < 0)
        {
            return PurchaseVerdicts.ShortfallRisk;
        }

        if (lowestAfter < buffer)
        {
            return PurchaseVerdicts.Consider;
        }

        if (status == PurchaseStatuses.Partial)
        {
            return PurchaseVerdicts.InsufficientData;
        }

        return input.Budget.Status == PurchaseImpactStatuses.Over || input.Plan.Status == PurchaseImpactStatuses.Over
            ? PurchaseVerdicts.OffPlan
            : PurchaseVerdicts.FitsPlan;
    }

    /// <summary>Giá cao nhất mua đúng ngày đó mà điểm thấp nhất trong kỳ vẫn không xuống dưới vùng đệm.</summary>
    private static long MaxAffordable(PurchaseVerdictInput input, long buffer)
    {
        var affected = input.Days.Where(d => d.Date >= input.PurchaseDate && d.Date <= input.Period.To).ToList();
        return affected.Count == 0 ? 0 : Math.Max(0, affected.Min(d => d.BeforeCents) - buffer);
    }

    /// <summary>Ngày mua sớm nhất sau ngày đã chọn mà khoản này không làm số dư xuống dưới vùng đệm trước kỳ thu nhập kế tiếp của ngày đó.</summary>
    private static DateOnly? EarliestSafeDate(PurchaseVerdictInput input, long buffer)
    {
        var last = input.Days[^1].Date;
        for (var day = input.PurchaseDate.AddDays(1); day <= last; day = day.AddDays(1))
        {
            var end = PeriodEnd(day, input.IncomeDates, input.HorizonDays, last);
            var lowest = input.Days.Where(d => d.Date >= day && d.Date <= end).Min(d => d.BeforeCents);
            if (lowest - input.AmountCents >= buffer)
            {
                return day;
            }
        }

        return null;
    }

    private static DateOnly PeriodEnd(DateOnly day, IReadOnlyList<DateOnly> incomeDates, int horizonDays, DateOnly last)
    {
        var upcoming = incomeDates.Where(d => d > day).ToList();
        if (upcoming.Count > 0)
        {
            return upcoming.Min().AddDays(-1);
        }

        var end = day.AddDays(horizonDays);
        return end > last ? last : end;
    }

    /// <summary>Câu kết luận ngắn bằng mẫu. Số liệu luôn lấy từ bộ tính, không qua AI, nên AI hay mạng lỗi vẫn có câu trả lời đúng.</summary>
    public static string Summary(PurchaseVerdictInput input, PurchaseVerdictResult result, string? item)
    {
        if (result.LowestAfter is null)
        {
            return "Chưa đủ dữ liệu để đánh giá khoản mua này. Hãy cập nhật ví, số dư và lịch thu nhập rồi thử lại.";
        }

        var what = string.IsNullOrWhiteSpace(item) ? "khoản này" : item.Trim();
        var purchase = $"Nếu mua {what} ({ForecastText.Money(input.AmountCents)}) ngày {ForecastText.Day(input.PurchaseDate)}";
        var period = $"{ForecastText.Day(input.Period.From)}–{ForecastText.Day(input.Period.To)}";
        var low = result.LowestAfter;
        var lowText = $"số dư dự kiến thấp nhất trong kỳ {period} là {ForecastText.Money(low.BalanceCents)} vào {ForecastText.Day(low.Day)}";

        return result.Verdict switch
        {
            PurchaseVerdicts.ShortfallRisk => $"{purchase}, {lowText}: có nguy cơ thiếu {ForecastText.Money(result.ShortfallCents)}.",
            PurchaseVerdicts.Consider => $"{purchase}, {lowText}, thấp hơn vùng đệm {ForecastText.Money(input.BufferCents)} bạn đặt. Cần cân nhắc.",
            PurchaseVerdicts.OffPlan => $"{purchase}, {lowText}. Vẫn đủ tiền nhưng lệch kế hoạch ngân sách hoặc kế hoạch tháng.",
            PurchaseVerdicts.FitsPlan => $"{purchase}, {lowText}. Phù hợp với kế hoạch hiện tại theo dữ liệu đang có.",
            _ => $"{purchase}, {lowText}. Dữ liệu chưa đủ để kết luận chắc chắn.",
        };
    }
}
