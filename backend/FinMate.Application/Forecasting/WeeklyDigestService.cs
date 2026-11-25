using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public interface IWeeklyDigestService
{
    /// <summary>
    /// Tổng kết tuần của một người dùng: ngân sách nào có nguy cơ vượt và mục tiêu gần hạn nhất đến đâu. Mỗi người tối đa một bản mỗi tuần (khoá theo ngày thứ Hai của tuần).
    /// Không có ngân sách lẫn mục tiêu thì không gửi gì: không có gì để tổng kết.
    /// </summary>
    Task<bool> SendAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Bản tin tuần đi qua hộp thư <c>forecast_alerts</c> (lưu trước, chống lặp, tôn trọng công tắc cảnh báo dự báo). Nội dung dựng bằng CODE từ số liệu đã có, không LLM, và chỉ nêu tên nhóm, số lượng, phần trăm:
/// không có số tiền nên không lộ trên màn hình khóa.
/// </summary>
public sealed class WeeklyDigestService(IBudgetForecastService budgets, ISavingGoalRepository goals, IForecastAlertDispatcher dispatcher) : IWeeklyDigestService
{
    private const int NamedBudgets = 3;

    public async Task<bool> SendAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await budgets.GetAsync(userId, ct);
        var goal = (await goals.GetListForUserAsync(userId, SavingGoalStatus.Active, ct))
            .OrderBy(g => g.Deadline ?? DateTimeOffset.MaxValue).ThenBy(g => g.Name, StringComparer.Ordinal).FirstOrDefault();
        if (rows.Count == 0 && goal is null)
        {
            return false;
        }

        var risky = rows.Where(b => b.Status is "at_risk" or "over").OrderByDescending(b => b.ProjectedPercent).ThenBy(b => b.CategoryName, StringComparer.Ordinal).ToList();
        var parts = new List<string>();
        if (rows.Count > 0)
        {
            parts.Add(risky.Count == 0
                ? "Tuần này chưa ngân sách nào có nguy cơ vượt."
                : $"{risky.Count} ngân sách có nguy cơ vượt: {string.Join(", ", risky.Take(NamedBudgets).Select(b => b.CategoryName ?? "Tổng chi tiêu"))}.");
        }

        if (goal is not null)
        {
            var percent = goal.TargetCents > 0 ? (goal.SavedCents * 100 + goal.TargetCents / 2) / goal.TargetCents : 0;
            parts.Add($"Mục tiêu \"{goal.Name}\" đã đạt {percent}%.");
        }

        var monday = MondayOf(VietnamTime.Today());
        var alert = await dispatcher.RaiseAsync(
            new ForecastAlertRequest(
                userId,
                ForecastAlertKind.WeeklyDigest,
                $"weekly_digest:{monday:yyyy-MM-dd}",
                "Tổng kết tuần của bạn",
                string.Join(" ", parts),
                new Dictionary<string, string> { ["week"] = monday.ToString("yyyy-MM-dd"), ["riskyBudgets"] = risky.Count.ToString() }),
            ct);
        return alert is not null;
    }

    internal static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
