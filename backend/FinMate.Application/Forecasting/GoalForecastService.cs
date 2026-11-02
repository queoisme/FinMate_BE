using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.SavingGoals;
using FinMate.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Forecasting;

public sealed record GoalForecastDto(
    Guid GoalId,
    string Name,
    string GoalStatus,
    long TargetCents,
    long SavedCents,
    long RemainingCents,
    int PercentComplete,
    DateOnly? Deadline,
    string ForecastStatus,
    long AverageMonthlyCents,
    long ConservativeMonthlyCents,
    int MonthsOfData,
    string Confidence,
    bool IsScenario,
    DateOnly? LikelyDate,
    DateOnly? ConservativeDate,
    long? RequiredMonthlyCents,
    string Message);

public interface IGoalForecastService
{
    /// <param name="scenarioMonthlyCents">Mức góp hằng tháng muốn thử (không lưu); null = theo lịch sử góp.</param>
    Task<GoalForecastDto> GetAsync(Guid userId, Guid goalId, long? scenarioMonthlyCents, CancellationToken ct = default);
}

public class GoalForecastService : IGoalForecastService
{
    private const int NormalConfidenceMonths = 3;
    private const long MaxScenarioCents = 100_000_000_000;

    private readonly ISavingGoalRepository _goals;
    private readonly ISystemConfigProvider _config;

    public GoalForecastService(ISavingGoalRepository goals, ISystemConfigProvider config)
    {
        _goals = goals;
        _config = config;
    }

    public async Task<GoalForecastDto> GetAsync(Guid userId, Guid goalId, long? scenarioMonthlyCents, CancellationToken ct = default)
    {
        if (scenarioMonthlyCents is <= 0 or > MaxScenarioCents)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(scenarioMonthlyCents), "Mức góp hằng tháng phải lớn hơn 0.")]);
        }

        var goal = await _goals.GetByIdAsync(goalId, userId, ct) ?? throw new NotFoundException("SavingGoal", goalId);
        var settings = (await _config.GetAsync(ct)).Document.Forecast;
        var today = VietnamTime.Today();
        var (months, fullMonths) = RateMonths(goal, today, settings.GoalRateMonths);
        var since = VietnamTime.MonthRange(months[0].Year, months[0].Month).Start;
        var contributions = await _goals.GetContributionsSinceAsync(goal.Id, userId, since, ct);
        var totals = months
            .Select(m => contributions.Where(c => InMonth(VietnamTime.DateOf(c.ContributedAt), m)).Sum(c => c.AmountCents))
            .ToList();

        var deadline = goal.Deadline is { } d ? VietnamTime.DateOf(d) : (DateOnly?)null;
        var forecast = GoalForecastCalculator.Compute(goal.TargetCents, goal.SavedCents, deadline, totals, today, scenarioMonthlyCents);
        return new GoalForecastDto(
            goal.Id,
            goal.Name,
            goal.Status.ToString().ToLowerInvariant(),
            goal.TargetCents,
            goal.SavedCents,
            forecast.RemainingCents,
            SavingGoalMapper.PercentComplete(goal),
            deadline,
            forecast.Status,
            forecast.AverageMonthlyCents,
            forecast.ConservativeMonthlyCents,
            forecast.MonthsOfData,
            fullMonths >= NormalConfidenceMonths || forecast.IsScenario ? "normal" : "low",
            forecast.IsScenario,
            forecast.LikelyDate,
            forecast.ConservativeDate,
            forecast.RequiredMonthlyCents,
            Message(forecast));
    }

    /// <summary>
    /// Các tháng trọn vẹn gần nhất (tối đa <paramref name="maxMonths"/>) kể từ tháng tạo mục tiêu. Mục tiêu tạo ngay tháng này thì chưa có tháng trọn vẹn nào:
    /// dùng tạm tháng đang chạy (độ tin cậy thấp).
    /// </summary>
    private static (List<DateOnly> Months, int FullMonths) RateMonths(SavingGoal goal, DateOnly today, int maxMonths)
    {
        var current = new DateOnly(today.Year, today.Month, 1);
        var createdDate = VietnamTime.DateOf(goal.CreatedAt);
        var created = new DateOnly(createdDate.Year, createdDate.Month, 1);
        var months = Enumerable.Range(1, maxMonths)
            .Select(i => current.AddMonths(-i))
            .Where(m => m >= created)
            .OrderBy(m => m)
            .ToList();
        return months.Count > 0 ? (months, months.Count) : ([current], 0);
    }

    private static string Message(GoalForecast f) => f.Status switch
    {
        GoalForecastStatus.Reached => "Bạn đã tích lũy đủ cho mục tiêu này.",
        GoalForecastStatus.Unreachable => f.RequiredMonthlyCents is { } need
            ? $"Với thói quen hiện tại chưa thể đạt mục tiêu. Cần góp khoảng {ForecastText.Money(need)}/tháng để kịp hạn."
            : "Với thói quen hiện tại chưa thể đạt mục tiêu. Hãy đặt một mức góp hằng tháng.",
        GoalForecastStatus.NoDeadline => $"Dự kiến đạt khoảng {ForecastText.Day(f.LikelyDate!.Value)}/{f.LikelyDate.Value.Year}.",
        GoalForecastStatus.OnTrack => $"Bạn sẽ kịp hạn: dự kiến đạt khoảng {ForecastText.Day(f.LikelyDate!.Value)}/{f.LikelyDate.Value.Year}.",
        _ => $"Có thể không kịp hạn. Cần góp khoảng {ForecastText.Money(f.RequiredMonthlyCents ?? 0)}/tháng.",
    };

    private static bool InMonth(DateOnly date, DateOnly monthStart) => date.Year == monthStart.Year && date.Month == monthStart.Month;
}
