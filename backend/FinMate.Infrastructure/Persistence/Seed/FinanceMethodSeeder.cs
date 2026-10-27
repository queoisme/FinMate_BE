using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Chín cách quản lý ban đầu (nguồn: finmate_khao_sat_insight_va_goi_y_quan_ly_tai_chinh.md, mục 3 — mỗi dòng bảng ánh xạ
/// một phương pháp). Idempotent theo <c>code</c>: chạy lại chỉ thêm phương pháp CHƯA có, không ghi đè nội dung, tham số hay
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
        string[] StarterSteps);

    /// <summary>Ngưỡng cảnh báo T1/T2 và giờ nhắc dự báo — chung cho mọi phương pháp có hạn mức. Mặc định 70%, 90%, 22:00.</summary>
    private static readonly Param[] AlertParams =
    [
        new(FinanceParamKeys.T1Pct, "Ngưỡng nhắc nhẹ (% hạn mức đã dùng)", FinanceParamTypes.Percent, 70, 1, 99),
        new(FinanceParamKeys.T2Pct, "Ngưỡng cảnh báo mạnh (% hạn mức đã dùng)", FinanceParamTypes.Percent, 90, 2, 99),   // 100% là mức "vượt hạn mức" cố định
        new(FinanceParamKeys.ForecastMinute, "Giờ nhắc dự báo hằng ngày (số phút từ 00:00)", FinanceParamTypes.Minutes, 1320, 0, 1439),
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
            ]),

        new(FinanceMethodCodes.CategoryBudget,
            "Ngân sách theo nhóm",
            "Đặt tỷ lệ cho nhu cầu, mong muốn và mục tiêu, rồi đặt hạn mức cho các nhóm chi chính. Tỷ lệ 50/30/20 chỉ là mẫu tham khảo.",
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
            + "Tỷ lệ 50/30/20 chỉ là mẫu tham khảo, bạn tự chọn tỷ lệ phù hợp với mình.",
            [
                "Chọn tỷ lệ giữa nhu cầu, mong muốn và mục tiêu.",
                "Đặt hạn mức cho vài nhóm chi chính.",
                "Xem chi thực tế mỗi tuần và chỉnh hạn mức nếu liên tục thiếu thực tế.",
            ]),

        new(FinanceMethodCodes.AvailableToSpend,
            "Số tiền còn được chi",
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
            "Ngân sách theo dòng tiền",
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
            "Phong bì cho vài nhóm chi",
            "Chỉ đặt hạn mức riêng cho vài nhóm chi dễ vượt, không cần chia mọi khoản.",
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
            "Quỹ mục tiêu và trả cho mình trước",
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
            "Quỹ dự phòng và quỹ chi phí không đều",
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
            ]),

        new(FinanceMethodCodes.LowEffortAutomation,
            "Tự động hóa và kiểm tra định kỳ",
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
