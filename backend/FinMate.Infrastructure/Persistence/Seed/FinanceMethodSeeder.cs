using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Các cách quản lý ban đầu: chín cách theo finmate_khao_sat_insight_va_goi_y_quan_ly_tai_chinh.md (mục 3 — mỗi dòng bảng ánh xạ
/// một phương pháp), đặt tên theo tên quen thuộc, cộng bốn phương pháp phổ biến thêm sau (6 hũ, 70/20/10, 80/20, Zero-Based). Idempotent theo <c>code</c>: chạy lại chỉ thêm phương pháp CHƯA có, không ghi đè nội dung, tham số hay
/// trạng thái <c>is_active</c> mà admin đã sửa.
///
/// <c>eligibility</c> = <c>{"weights":{signal:trọng số},"excludes":[signal]}</c>. <c>weights</c> là dấu hiệu CỘNG ĐIỂM cho phương
/// pháp (không phải điều kiện bắt buộc); <c>excludes</c> là dấu hiệu LOẠI HẲN phương pháp khỏi shortlist. Thứ tự ưu tiên của file .md
/// được cài bằng <c>excludes</c>: người đang thiếu hụt dòng tiền hoặc trễ nghĩa vụ không bị gợi ý tỷ lệ cứng hay mục tiêu tiết kiệm.
/// </summary>
public static class FinanceMethodSeeder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Param(string Key, string Label, string Type, long Default, long Min, long Max, bool UserEditable = true);

    private sealed record MethodSeed(
        string Code,
        string Name,
        string Summary,
        FinanceMethodRole Role,
        GoalMode GoalMode,
        Param[] Params,
        (string Signal, int Weight)[] Weights,
        string[] Excludes,
        string WhyText,
        string[] StarterSteps,
        string? HomeLayout = null);

    /// <summary>Cách chỉ theo dõi (chưa có hạn mức) thì không có "tiền khả dụng" để khoe: bắt đầu bằng tham số và các bước.</summary>
    private const string TrackOnlyLayout = """[{"type":"params"},{"type":"starter_steps"}]""";

    private const string CategoryBudgetLayout =
        """
        [{"type":"spendable"},
         {"type":"params","title":"Chia thu nhập","keys":["needs_pct","wants_pct","goals_pct"],"display":"allocation"},
         {"type":"params","title":"Ngưỡng cảnh báo","keys":["t1_pct","t2_pct"]},
         {"type":"goals"}]
        """;

    /// <summary>Hũ → danh mục hệ thống (slug). Hũ tự do tài chính, tiết kiệm dài hạn và cho đi chưa gán danh mục chi nào nên luôn 0 đã chi. Admin sửa được ở trang chủ của phương pháp.</summary>
    private const string SixJarsLayout =
        """
        [{"type":"spendable"},
         {"type":"jars","title":"6 chiếc hũ","jars":[
           {"key":"necessities_pct","label":"Thiết yếu","categories":["food","housing","bills","transport","health","family"]},
           {"key":"education_pct","label":"Giáo dục","categories":["education"]},
           {"key":"play_pct","label":"Hưởng thụ","categories":["entertainment","shopping"]},
           {"key":"financial_freedom_pct","label":"Tự do tài chính","categories":[]},
           {"key":"long_term_pct","label":"Tiết kiệm dài hạn","categories":[]},
           {"key":"give_pct","label":"Cho đi","categories":[]}]},
         {"type":"params","title":"Ngưỡng cảnh báo","keys":["t1_pct","t2_pct"]},
         {"type":"goals"}]
        """;

    private const string Rule702010Layout =
        """
        [{"type":"spendable"},
         {"type":"params","title":"Chia thu nhập","keys":["living_pct","saving_pct","debt_goal_pct"],"display":"allocation"},
         {"type":"params","title":"Ngưỡng cảnh báo","keys":["t1_pct","t2_pct"]},
         {"type":"goals"}]
        """;

    /// <summary>Ngưỡng cảnh báo T1/T2 và giờ nhắc dự báo — chung cho mọi phương pháp có hạn mức. Mặc định 70%, 90%, 22:00.</summary>
    private static readonly Param[] AlertParams =
    [
        new(FinanceParamKeys.T1Pct, "Ngưỡng nhắc nhẹ (% hạn mức đã dùng)", FinanceParamTypes.Percent, 70, 1, 99),
        new(FinanceParamKeys.T2Pct, "Ngưỡng cảnh báo mạnh (% hạn mức đã dùng)", FinanceParamTypes.Percent, 90, 2, 99),   // 100% là mức "vượt hạn mức" cố định
        new(FinanceParamKeys.ForecastMinute, "Giờ nhắc dự báo hằng ngày", FinanceParamTypes.Minutes, 1320, 0, 1439),
    ];

    private const long MaxCents = 1_000_000_000;

    private static readonly MethodSeed[] Methods =
    [
        new(FinanceMethodCodes.MinimalTracking,
            "Theo dõi chi tiêu tối giản",
            "Chỉ theo dõi 3–5 nhóm chi chính trong vài tuần và xem tổng kết mỗi tuần, chưa đặt hạn mức.",
            FinanceMethodRole.Primary, GoalMode.TrackOnly,
            [
                new("tracked_groups", "Số nhóm chi theo dõi", FinanceParamTypes.Int, 4, 3, 5),
                new("observe_weeks", "Số tuần quan sát trước khi thử hạn mức", FinanceParamTypes.Int, 3, 2, 4),
            ],
            [
                (FinanceSignals.LowVisibility, 3), (FinanceSignals.AbandonsTracking, 2), (FinanceSignals.LowCommitment, 2),
                (FinanceSignals.UntriedLimits, 1), (FinanceSignals.GoalUnclear, 1), (FinanceSignals.WantsSelfService, 1),
                (FinanceSignals.TimeMonthly, 1),
            ],
            [],
            "Bạn chưa rõ tiền đang đi đâu hoặc từng bỏ ghi chép giữa chừng, nên bước đầu hợp lý là chỉ quan sát vài nhóm chi chính. "
            + "Khi đã đủ dữ liệu, bạn có thể thử đặt hạn mức. Bạn có thể đổi cách quản lý bất cứ lúc nào.",
            [
                "Chọn 3–5 nhóm chi bạn muốn theo dõi.",
                "Để FinMate ghi nhận giao dịch tự động và chỉ sửa nhóm khi cần.",
                "Xem tổng kết tuần; sau 2–4 tuần hãy thử đặt hạn mức.",
            ],
            TrackOnlyLayout),

        new(FinanceMethodCodes.CategoryBudget,
            "Quy tắc 50/30/20",
            "Chia thu nhập: 50% cho nhu cầu thiết yếu, 30% cho mong muốn, 20% cho tiết kiệm và trả nợ. Tỷ lệ chỉnh được theo hoàn cảnh của bạn.",
            FinanceMethodRole.Primary, GoalMode.ReserveFirst,
            [
                .. AlertParams,
                new("needs_pct", "Tỷ lệ cho nhu cầu (%)", FinanceParamTypes.Percent, 50, 0, 100),
                new("wants_pct", "Tỷ lệ cho mong muốn (%)", FinanceParamTypes.Percent, 30, 0, 100),
                new("goals_pct", "Tỷ lệ cho mục tiêu (%)", FinanceParamTypes.Percent, 20, 0, 100),
            ],
            [
                (FinanceSignals.IncomeStable, 2), (FinanceSignals.SurplusRegular, 3), (FinanceSignals.LikesLimits, 3),
                (FinanceSignals.WantsCategoryLimits, 3), (FinanceSignals.TracksClosely, 2), (FinanceSignals.TimeDaily, 2),
                (FinanceSignals.TimeWeekly, 1), (FinanceSignals.GoalSpecificTarget, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.AbandonsTracking, FinanceSignals.LowCommitment],
            "Thu nhập của bạn khá ổn định, thường còn dư và bạn thích có hạn mức rõ ràng cho từng nhóm chi. "
            + "50/30/20 là điểm xuất phát dễ nhớ, bạn tự chỉnh tỷ lệ cho phù hợp với mình.",
            [
                "Chọn tỷ lệ giữa nhu cầu, mong muốn và mục tiêu (mặc định 50/30/20).",
                "Đặt hạn mức cho vài nhóm chi chính.",
                "Xem chi thực tế mỗi tuần và chỉnh hạn mức nếu liên tục thiếu thực tế.",
            ],
            CategoryBudgetLayout),

        new(FinanceMethodCodes.AvailableToSpend,
            "Số tiền an toàn để chi (Safe-to-spend)",
            "FinMate trừ hóa đơn và khoản để dành khỏi tiền sẵn có, rồi chỉ cho bạn một con số: còn bao nhiêu được chi.",
            FinanceMethodRole.Primary, GoalMode.AvailableToSpend,
            [
                .. AlertParams,
                new("reserve_cents", "Khoản giữ lại cố định mỗi kỳ (đồng)", FinanceParamTypes.Cents, 0, 0, MaxCents),
            ],
            [
                (FinanceSignals.WantsSingleNumber, 3), (FinanceSignals.NeedsFlexibleLimits, 2), (FinanceSignals.DislikesLimits, 2),
                (FinanceSignals.TimeMonthly, 1), (FinanceSignals.TracksViaBank, 1), (FinanceSignals.IncomeStable, 1),
                (FinanceSignals.LowVisibility, 1),
            ],
            [],
            "Bạn muốn một con số đơn giản thay vì nhiều hạn mức. Số tiền còn được chi được tính lại khi thu nhập hoặc hóa đơn thay đổi.",
            [
                "Nhập các khoản bắt buộc (hóa đơn, tiền thuê nhà) và khoản muốn để dành.",
                "FinMate trừ chúng khỏi tiền sẵn có và hiển thị số còn lại.",
                "Xem số này mỗi khi cần chi; cập nhật khi thu nhập hoặc hóa đơn đổi.",
            ]),

        new(FinanceMethodCodes.CashFlowBudget,
            "Ngân sách theo dòng tiền (Cash Flow)",
            "Lập kế hoạch từ số tiền đã nhận và lịch hóa đơn đến hạn, để biết số tiền an toàn có thể chi đến lần nhận tiền tiếp theo.",
            FinanceMethodRole.Primary, GoalMode.AvailableToSpend,
            [
                .. AlertParams,
                new("conservative_income_pct", "Lập kế hoạch theo % thu nhập thận trọng", FinanceParamTypes.Percent, 80, 50, 100),
                new("bill_reminder_days", "Nhắc hóa đơn trước (ngày)", FinanceParamTypes.Int, 3, 1, 14),
            ],
            [
                // Trọng số cao hơn các cách chọn theo SỞ THÍCH: file .md đặt khả năng chi trả và dòng tiền lên trước sở thích về mức chi tiết.
                (FinanceSignals.IncomeVariable, 5), (FinanceSignals.NoStableIncome, 3), (FinanceSignals.TightCashFlow, 4),
                (FinanceSignals.OccasionalShortfall, 3), (FinanceSignals.CashShortfall, 5), (FinanceSignals.WantsBillAlerts, 3),
                (FinanceSignals.OverdueObligations, 3), (FinanceSignals.NotificationsOnly, 1), (FinanceSignals.FixedCostsPressure, 2),
            ],
            [],
            "Thu nhập của bạn thay đổi theo kỳ hoặc đôi khi thiếu cuối tháng. Hãy bắt đầu bằng số tiền có thể chi đến lần nhận tiền tiếp theo, "
            + "dựa trên khoản đã nhận chứ không dựa vào thu nhập chưa chắc chắn.",
            [
                "Cập nhật khoản thu mỗi khi bạn nhận tiền.",
                "Thêm hóa đơn và khoản cố định sắp đến hạn để FinMate trừ ra.",
                "Bật nhắc hóa đơn và cảnh báo khi có nguy cơ thiếu tiền.",
            ]),

        new(FinanceMethodCodes.EnvelopeBudget,
            "Phương pháp phong bì (Envelope)",
            "Chia tiền thành các \"phong bì\" cho vài nhóm chi dễ vượt; hết phong bì nào thì dừng chi nhóm đó, không cần chia mọi khoản.",
            FinanceMethodRole.Both, GoalMode.Envelope,
            [
                .. AlertParams,
                new("envelope_count", "Số nhóm chi có phong bì", FinanceParamTypes.Int, 3, 1, 5),
            ],
            [
                (FinanceSignals.FlexibleSpendLeak, 3), (FinanceSignals.SelectiveLimits, 3), (FinanceSignals.NeedsFlexibleLimits, 1),
                (FinanceSignals.LikesLimits, 1), (FinanceSignals.SurplusRegular, 1),
            ],
            [],
            "Bạn dễ chi vượt ở một vài khoản hằng ngày. Đặt hạn mức riêng cho những khoản đó thay vì quản lý mọi thứ.",
            [
                "Chọn 1–3 nhóm chi bạn dễ vượt nhất.",
                "Đặt hạn mức cho từng nhóm theo kỳ bạn muốn.",
                "Nếu hạn mức liên tục thiếu thực tế, hãy điều chỉnh thay vì bỏ cuộc.",
            ]),

        new(FinanceMethodCodes.GoalFund,
            "Trả cho mình trước (Pay Yourself First)",
            "Tách khoản dành cho mục tiêu ngay khi có tiền vào, rồi theo dõi tiến độ.",
            FinanceMethodRole.Supporting, GoalMode.ReserveFirst,
            [
                new("saving_pct", "Tỷ lệ để dành từ thu nhập thực nhận (%)", FinanceParamTypes.Percent, 10, 1, 50),
            ],
            [
                (FinanceSignals.GoalSpecificTarget, 3), (FinanceSignals.SavesAutomatically, 3), (FinanceSignals.SavesFixedAmount, 2),
                (FinanceSignals.SavesPercentOfIncome, 2), (FinanceSignals.SurplusRegular, 2), (FinanceSignals.WantsGoalTracking, 3),
                (FinanceSignals.IncomeStable, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.CannotSaveNow],
            "Bạn có mục tiêu cụ thể và muốn để dành đều đặn. Tách khoản để dành ngay khi nhận tiền giúp mục tiêu không bị chi tiêu hằng ngày lấn át. "
            + "Mức đóng góp chỉ nên đặt sau khi đã xét chi thiết yếu và nghĩa vụ.",
            [
                "Chọn một mục tiêu và mức bạn muốn để dành mỗi kỳ.",
                "Tách khoản này ngay khi có tiền vào.",
                "Xem tiến độ mục tiêu và điều chỉnh mức đóng góp khi cần.",
            ]),

        new(FinanceMethodCodes.EmergencyBuffer,
            "Quỹ khẩn cấp (Emergency Fund)",
            "Tích lũy nhỏ và đều cho khoản dự phòng khẩn cấp, và để dành riêng cho các chi phí đã biết nhưng không hằng tháng.",
            FinanceMethodRole.Supporting, GoalMode.ReserveFirst,   // công cụ BỔ TRỢ (file .md: "quỹ dự phòng nhỏ" đi kèm cách chính), không tự đứng làm cách chính
            [
                new("starter_weeks", "Quỹ đệm khởi đầu (số tuần chi phí thiết yếu)", FinanceParamTypes.Int, 1, 1, 4),
                new("contribution_pct", "Tỷ lệ thu nhập để dành cho quỹ (%)", FinanceParamTypes.Percent, 5, 1, 30),
            ],
            [
                (FinanceSignals.ThinBuffer, 3), (FinanceSignals.BufferUnseparated, 3), (FinanceSignals.IrregularExpenses, 3),
                (FinanceSignals.GoalEmergencyFund, 3),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations],
            "Vùng đệm của bạn còn mỏng hoặc chưa tách riêng, và có những khoản chi phát sinh không định kỳ. "
            + "Chia nhỏ khoản tích lũy giúp bạn không phải vay khi có chi bất ngờ.",
            [
                "Bắt đầu bằng quỹ đệm nhỏ, khoảng một tuần chi phí thiết yếu.",
                "Tách quỹ khẩn cấp khỏi các khoản để dành khác.",
                "Thêm quỹ riêng cho chi phí đã biết nhưng không hằng tháng.",
            ]),

        new(FinanceMethodCodes.DebtRepayment,
            "Kế hoạch trả nợ",
            "Liệt kê các khoản nợ, ngày đến hạn và số tối thiểu để trả đúng hạn và chọn thứ tự trả phù hợp.",
            FinanceMethodRole.Primary, GoalMode.TrackOnly,
            [
                new("repayment_order", "Thứ tự trả: 1 = lãi cao trước, 2 = dư nợ nhỏ trước", FinanceParamTypes.Int, 1, 1, 2),
                new("extra_payment_cents", "Số tiền trả thêm mỗi kỳ (đồng)", FinanceParamTypes.Cents, 0, 0, MaxCents),
            ],
            [
                // Nợ là tín hiệu ưu tiên cao nhất sau khả năng chi trả (file .md mục 3): người có đủ 3D, 6D, 7C phải nhận kế hoạch trả nợ làm cách chính.
                (FinanceSignals.DebtPressure, 4), (FinanceSignals.GoalDebtReduction, 3), (FinanceSignals.OverdueObligations, 3),
                (FinanceSignals.CashShortfall, 1),
            ],
            [],
            "Bạn đang chịu áp lực từ nợ, nên việc đầu tiên là biết rõ từng khoản nợ và ngày đến hạn. "
            + "FinMate chỉ cung cấp thông tin chung; nếu nợ quá tải, hãy cân nhắc hỗ trợ từ người có chuyên môn.",
            [
                "Liệt kê từng khoản nợ: dư nợ, lãi, số tiền tối thiểu, ngày đến hạn.",
                "Trả đủ số tối thiểu đúng hạn.",
                "So sánh thứ tự trả và chọn cách bạn duy trì được.",
            ],
            TrackOnlyLayout),

        new(FinanceMethodCodes.LowEffortAutomation,
            "Tự động hóa tài chính",
            "Ít nhóm chi, nhắc ngắn và tổng kết định kỳ để bạn duy trì được với rất ít thao tác.",
            FinanceMethodRole.Both, GoalMode.ReserveFirst,
            [
                new("review_cadence_days", "Chu kỳ tổng kết (ngày)", FinanceParamTypes.Int, 7, 7, 30),
                new("max_reminders_per_week", "Số lần nhắc tối đa mỗi tuần", FinanceParamTypes.Int, 3, 0, 7),
            ],
            [
                (FinanceSignals.LowCommitment, 3), (FinanceSignals.AbandonsTracking, 3), (FinanceSignals.TimeMonthly, 3),
                (FinanceSignals.NotificationsOnly, 3), (FinanceSignals.TimeWeekly, 1), (FinanceSignals.LowVisibility, 1),
            ],
            [],
            "Bạn ít thời gian hoặc từng bỏ cuộc giữa chừng, nên cách phù hợp là để FinMate làm phần lớn việc và chỉ nhắc khi cần.",
            [
                "Chỉ theo dõi vài nhóm chi lớn nhất.",
                "Nhận nhắc ngắn và thông báo quan trọng.",
                "Xem tổng kết tuần hoặc tháng; giảm tần suất nếu thông báo gây phiền.",
            ]),

        new(FinanceMethodCodes.SixJars,
            "6 chiếc hũ (6 Jars)",
            "Chia thu nhập vào 6 hũ: thiết yếu, giáo dục, hưởng thụ, tự do tài chính, tiết kiệm dài hạn và cho đi. Mỗi hũ có phần trăm riêng và theo dõi chi tiêu của riêng nó.",
            FinanceMethodRole.Primary, GoalMode.ReserveFirst,
            [
                .. AlertParams,
                new("necessities_pct", "Hũ thiết yếu (%)", FinanceParamTypes.Percent, 55, 0, 100),
                new("education_pct", "Hũ giáo dục (%)", FinanceParamTypes.Percent, 10, 0, 100),
                new("play_pct", "Hũ hưởng thụ (%)", FinanceParamTypes.Percent, 10, 0, 100),
                new("financial_freedom_pct", "Hũ tự do tài chính (%)", FinanceParamTypes.Percent, 10, 0, 100),
                new("long_term_pct", "Hũ tiết kiệm dài hạn (%)", FinanceParamTypes.Percent, 10, 0, 100),
                new("give_pct", "Hũ cho đi (%)", FinanceParamTypes.Percent, 5, 0, 100),
            ],
            [
                (FinanceSignals.LikesLimits, 2), (FinanceSignals.WantsCategoryLimits, 2), (FinanceSignals.TracksClosely, 2),
                (FinanceSignals.SurplusRegular, 2), (FinanceSignals.IncomeStable, 2), (FinanceSignals.SavesPercentOfIncome, 2),
                (FinanceSignals.GoalSpecificTarget, 1), (FinanceSignals.TimeWeekly, 1), (FinanceSignals.TimeDaily, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.AbandonsTracking, FinanceSignals.LowCommitment],
            "Thu nhập của bạn khá ổn định và bạn muốn quản lý toàn diện: vừa chi tiêu, vừa học tập, vừa tích lũy và cho đi. "
            + "Mỗi hũ có phần trăm riêng nên bạn thấy ngay hũ nào sắp cạn.",
            [
                "Chia thu nhập vào 6 hũ (mặc định 55/10/10/10/10/5, bạn chỉnh được).",
                "Mỗi khi chi tiêu, FinMate tính vào hũ tương ứng với nhóm chi.",
                "Xem các hũ mỗi tuần và chỉnh phần trăm nếu một hũ liên tục thiếu hoặc dư.",
            ],
            SixJarsLayout),

        new(FinanceMethodCodes.Rule702010,
            "Quy tắc 70/20/10",
            "Chia thu nhập: 70% cho sinh hoạt, 20% cho tiết kiệm, 10% cho trả nợ hoặc mục tiêu khác. Đơn giản hơn 50/30/20.",
            FinanceMethodRole.Primary, GoalMode.ReserveFirst,
            [
                .. AlertParams,
                new("living_pct", "Sinh hoạt (%)", FinanceParamTypes.Percent, 70, 0, 100),
                new("saving_pct", "Tiết kiệm (%)", FinanceParamTypes.Percent, 20, 0, 100),
                new("debt_goal_pct", "Trả nợ hoặc mục tiêu khác (%)", FinanceParamTypes.Percent, 10, 0, 100),
            ],
            [
                (FinanceSignals.SavesPercentOfIncome, 3), (FinanceSignals.IncomeStable, 2), (FinanceSignals.SurplusRegular, 2),
                (FinanceSignals.DebtPressure, 2), (FinanceSignals.GoalDebtReduction, 1), (FinanceSignals.TimeWeekly, 1),
                (FinanceSignals.LowVisibility, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.CannotSaveNow],
            "Bạn thích một công thức đơn giản chỉ có ba phần. Phần 10% giúp bạn vừa trả nợ vừa có mục tiêu khác mà không phải chia quá nhiều.",
            [
                "Chọn tỷ lệ giữa sinh hoạt, tiết kiệm và trả nợ hoặc mục tiêu khác (mặc định 70/20/10).",
                "Chuyển phần tiết kiệm và phần trả nợ ngay khi nhận thu nhập.",
                "Xem phần sinh hoạt mỗi tuần; chỉnh tỷ lệ nếu liên tục thiếu thực tế.",
            ],
            Rule702010Layout),

        new(FinanceMethodCodes.Rule8020,
            "Quy tắc 80/20 (tiết kiệm trước)",
            "Tiết kiệm 20% thu nhập ngay khi nhận, phần 80% còn lại chi tiêu thoải mái mà không cần theo dõi chi tiết từng khoản.",
            FinanceMethodRole.Primary, GoalMode.ReserveFirst,
            [
                .. AlertParams,
                new("saving_pct", "Tỷ lệ tiết kiệm trước (%)", FinanceParamTypes.Percent, 20, 1, 50),
            ],
            [
                (FinanceSignals.DislikesLimits, 3), (FinanceSignals.LowCommitment, 2), (FinanceSignals.SavesAutomatically, 3),
                (FinanceSignals.SavesPercentOfIncome, 2), (FinanceSignals.TimeMonthly, 2), (FinanceSignals.LowVisibility, 1),
                (FinanceSignals.IncomeStable, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.CannotSaveNow],
            "Bạn không thích theo dõi chi tiết. Chỉ cần tiết kiệm phần của mình trước, phần còn lại bạn chi tự do.",
            [
                "Chọn tỷ lệ tiết kiệm (mặc định 20%).",
                "Chuyển khoản tiết kiệm ngay khi nhận thu nhập.",
                "Phần còn lại là tiền được chi; chỉ cần xem tổng kết mỗi tháng.",
            ]),

        new(FinanceMethodCodes.ZeroBased,
            "Ngân sách từ con số 0 (Zero-Based)",
            "Mỗi đồng thu nhập đều được gán một mục đích cụ thể (chi tiêu, tiết kiệm, trả nợ) cho đến khi phần còn lại bằng 0.",
            FinanceMethodRole.Primary, GoalMode.ReserveFirst,
            [
                .. AlertParams,
                new("plan_review_days", "Chu kỳ rà soát kế hoạch (ngày)", FinanceParamTypes.Int, 7, 1, 31),
            ],
            [
                (FinanceSignals.TracksClosely, 3), (FinanceSignals.LikesLimits, 3), (FinanceSignals.WantsCategoryLimits, 3),
                (FinanceSignals.TimeDaily, 3), (FinanceSignals.IncomeStable, 2), (FinanceSignals.SurplusRegular, 1),
            ],
            [FinanceSignals.CashShortfall, FinanceSignals.OverdueObligations, FinanceSignals.AbandonsTracking, FinanceSignals.LowCommitment],
            "Bạn muốn kiểm soát chặt và sẵn sàng dành thời gian mỗi ngày. Gán mục đích cho từng đồng giúp bạn biết tiền đi đâu và không còn khoản \"mất dấu\".",
            [
                "Liệt kê mọi khoản thu và mọi khoản chi dự kiến của kỳ này.",
                "Gán mục đích cho đến khi thu nhập trừ đi kế hoạch bằng 0.",
                "Rà soát kế hoạch theo chu kỳ và chuyển tiền giữa các khoản khi cần.",
            ]),
    ];

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existing = await context.FinanceMethods.Select(m => m.Code).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = false;

        for (var i = 0; i < Methods.Length; i++)
        {
            var seed = Methods[i];
            if (existing.Contains(seed.Code))
            {
                continue;
            }

            context.FinanceMethods.Add(new FinanceMethod
            {
                Id = Guid.NewGuid(),
                Code = seed.Code,
                Name = seed.Name,
                Summary = seed.Summary,
                Role = seed.Role,
                GoalMode = seed.GoalMode,
                ParamDefs = JsonSerializer.Serialize(seed.Params, Json),
                Eligibility = JsonSerializer.Serialize(
                    new
                    {
                        weights = seed.Weights.ToDictionary(w => w.Signal, w => w.Weight),
                        excludes = seed.Excludes,
                    },
                    Json),
                WhyText = seed.WhyText,
                StarterSteps = JsonSerializer.Serialize(seed.StarterSteps, Json),
                HomeLayout = seed.HomeLayout ?? FinanceHomeBlocks.DefaultLayout,
                SortOrder = i + 1,
                IsActive = true,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(ct);
        }
    }
}
