using System.Reflection;

namespace FinMate.Domain.Entities;

/// <summary>
/// Bộ từ vựng signals: những "dấu hiệu" suy ra từ câu trả lời khảo sát (<c>survey_options.signals</c>) và được
/// <c>finance_methods.eligibility</c> / <c>method_goal_templates.conditions</c> tham chiếu. Một nơi duy nhất để
/// seeder, bộ chọn phương pháp và test cùng dùng, vì sai chính tả một signal là hỏng im lặng: phương pháp không
/// bao giờ khớp mà không có gì báo lỗi. Admin thêm signal mới trong DB được, nhưng signal nào code cần đọc (loại
/// trừ an toàn như <see cref="CashShortfall"/>) phải có hằng ở đây.
/// </summary>
public static class FinanceSignals
{
    // Dòng tiền
    public const string IncomeStable = "income_stable";
    public const string IncomeVariable = "income_variable";
    public const string NoStableIncome = "no_stable_income";
    public const string SurplusRegular = "surplus_regular";
    public const string TightCashFlow = "tight_cash_flow";
    /// <summary>Thỉnh thoảng thiếu (Q2D). KHÁC <see cref="CashShortfall"/>: không loại phương pháp nào, chỉ cộng điểm cho cách theo dòng tiền.</summary>
    public const string OccasionalShortfall = "occasional_shortfall";

    /// <summary>Thường xuyên thiếu hoặc trễ khoản phải trả (Q2E). Là tín hiệu ưu tiên an toàn: loại các phương pháp tiết kiệm và tỷ lệ cứng khỏi shortlist.</summary>
    public const string CashShortfall = "cash_shortfall";
    public const string OverdueObligations = "overdue_obligations";

    // Vùng đệm và nợ
    public const string HasBuffer = "has_buffer";
    public const string ThinBuffer = "thin_buffer";
    public const string BufferUnseparated = "buffer_unseparated";
    public const string DebtPressure = "debt_pressure";

    // Thói quen theo dõi và phản ứng với giới hạn
    public const string TracksClosely = "tracks_closely";
    public const string TracksViaBank = "tracks_via_bank";
    public const string LowVisibility = "low_visibility";
    public const string AbandonsTracking = "abandons_tracking";
    public const string LikesLimits = "likes_limits";
    public const string NeedsFlexibleLimits = "needs_flexible_limits";
    public const string SelectiveLimits = "selective_limits";
    public const string DislikesLimits = "dislikes_limits";
    public const string UntriedLimits = "untried_limits";

    // Khoản khó kiểm soát
    public const string FlexibleSpendLeak = "flexible_spend_leak";
    public const string FixedCostsPressure = "fixed_costs_pressure";
    public const string IrregularExpenses = "irregular_expenses";

    // Mục tiêu và cách để dành
    public const string GoalCashFlowControl = "goal_cash_flow_control";
    public const string GoalEmergencyFund = "goal_emergency_fund";
    public const string GoalDebtReduction = "goal_debt_reduction";
    public const string GoalSpecificTarget = "goal_specific_target";
    public const string GoalUnclear = "goal_unclear";
    public const string SavesAutomatically = "saves_automatically";
    public const string SavesFixedAmount = "saves_fixed_amount";
    public const string SavesPercentOfIncome = "saves_percent_of_income";
    public const string SavesLeftover = "saves_leftover";
    public const string CannotSaveNow = "cannot_save_now";

    // Thời gian dành ra và kiểu hỗ trợ mong muốn
    public const string TimeDaily = "time_daily";
    public const string TimeWeekly = "time_weekly";
    public const string TimeMonthly = "time_monthly";
    public const string NotificationsOnly = "notifications_only";
    public const string LowCommitment = "low_commitment";
    public const string WantsCategoryLimits = "wants_category_limits";
    public const string WantsSingleNumber = "wants_single_number";
    public const string WantsBillAlerts = "wants_bill_alerts";
    public const string WantsGoalTracking = "wants_goal_tracking";
    public const string WantsSelfService = "wants_self_service";

    /// <summary>Mọi signal đã khai báo ở trên — để test chặn sai chính tả trong dữ liệu seed.</summary>
    public static IReadOnlySet<string> All { get; } = typeof(FinanceSignals)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet();
}

/// <summary>Mã của các cách quản lý ban đầu (<c>finance_methods.code</c>, bất biến). Mỗi mã ứng với một nhánh code, nên là hằng chứ không phải chuỗi tự do.</summary>
public static class FinanceMethodCodes
{
    public const string MinimalTracking = "minimal_tracking";
    public const string CategoryBudget = "category_budget";
    public const string AvailableToSpend = "available_to_spend";
    public const string CashFlowBudget = "cash_flow_budget";
    public const string EnvelopeBudget = "envelope_budget";
    public const string GoalFund = "goal_fund";
    public const string EmergencyBuffer = "emergency_buffer";
    public const string DebtRepayment = "debt_repayment";
    public const string LowEffortAutomation = "low_effort_automation";
    public const string SixJars = "six_jars";
    public const string Rule702010 = "rule_70_20_10";
    public const string Rule8020 = "rule_80_20";
    public const string ZeroBased = "zero_based_budget";
}

/// <summary>Mã của các mẫu mục tiêu ban đầu (<c>goal_templates.code</c>, bất biến).</summary>
public static class GoalTemplateCodes
{
    public const string StarterEmergencyBuffer = "starter_emergency_buffer";
    public const string EmergencyFundThreeMonths = "emergency_fund_three_months";
    public const string IrregularExpensesFund = "irregular_expenses_fund";
    public const string SpecificTargetGoal = "specific_target_goal";
}

/// <summary>Kiểu giá trị của một tham số trong <c>finance_methods.param_defs</c>. Tất cả đều là SỐ NGUYÊN — không float.</summary>
public static class FinanceParamTypes
{
    public const string Percent = "percent";
    public const string Int = "int";

    /// <summary>Số tiền theo đơn vị đồng VND (cùng đơn vị với mọi cột <c>*_cents</c>).</summary>
    public const string Cents = "cents";

    /// <summary>Số phút kể từ 00:00 (22:00 = 1320).</summary>
    public const string Minutes = "minutes";
}

/// <summary>
/// Từ vựng KHỐI GIAO DIỆN của trang chủ một cách quản lý (<c>finance_methods.home_layout</c>). App có sẵn code vẽ từng loại khối; admin chỉ chọn khối nào, thứ tự nào,
/// nên thêm một cách quản lý mới không cần phát hành lại app. Thêm LOẠI khối mới thì cần code ở app, nên danh sách này là hằng.
/// </summary>
public static class FinanceHomeBlocks
{
    /// <summary>Tiền khả dụng của tháng (thu nhập, đã chi, đã dành cho mục tiêu). Nguồn: <c>GET /me/spendable</c>.</summary>
    public const string Spendable = "spendable";

    /// <summary>Tham số của cách đang dùng (giá trị đang có hiệu lực). Có thể lọc theo <c>keys</c> và vẽ dạng <c>list</c> hoặc <c>allocation</c> (chia phần trăm thu nhập).</summary>
    public const string Params = "params";

    /// <summary>
    /// Các "hũ": mỗi hũ là MỘT tham số kiểu % của cách quản lý (phần trăm thu nhập được chia cho hũ) cộng danh sách danh mục hệ thống thuộc hũ đó.
    /// App so chi tiêu tháng này của các danh mục trong hũ với số tiền được chia. Nguồn: <c>GET /me/jars</c>. Cấu hình ở chính khối: <c>jars: [{key, label?, categories: [slug]}]</c>.
    /// </summary>
    public const string Jars = "jars";

    /// <summary>Mục tiêu đang dành tiền và mẫu mục tiêu gợi ý. Nguồn: <c>GET /me/spendable</c>, <c>GET /me/goal-suggestions</c>.</summary>
    public const string Goals = "goals";

    /// <summary>Các bước bắt đầu của cách quản lý (<c>starter_steps</c>).</summary>
    public const string StarterSteps = "starter_steps";

    public const string DisplayList = "list";
    public const string DisplayAllocation = "allocation";

    public const int MaxBlocks = 8;
    public const int MaxTitleLength = 60;

    public const int MaxJars = 8;
    public const int MaxJarLabelLength = 40;
    public const int MaxCategoriesPerJar = 30;

    public static readonly IReadOnlyList<string> Types = [Spendable, Params, Jars, Goals, StarterSteps];

    /// <summary>Bố cục dùng khi admin chưa chọn gì.</summary>
    public const string DefaultLayout = """[{"type":"spendable"},{"type":"params"},{"type":"goals"},{"type":"starter_steps"}]""";
}

/// <summary>Khóa tham số mà code đọc trực tiếp (đánh giá cảnh báo, lịch nhắc). Tham số khác chỉ là cấu hình của riêng phương pháp.</summary>
public static class FinanceParamKeys
{
    /// <summary>Ngưỡng nhắc nhẹ, tính theo % hạn mức đã dùng. Mặc định 70.</summary>
    public const string T1Pct = "t1_pct";

    /// <summary>Ngưỡng cảnh báo mạnh, tính theo % hạn mức đã dùng. Mặc định 90. Luôn lớn hơn <see cref="T1Pct"/>.</summary>
    public const string T2Pct = "t2_pct";

    /// <summary>Giờ nhắc dự báo hằng ngày, theo số phút kể từ 00:00. Mặc định 1320 (22:00).</summary>
    public const string ForecastMinute = "forecast_minute";
}
