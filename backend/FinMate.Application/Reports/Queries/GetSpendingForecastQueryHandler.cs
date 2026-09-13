using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

public class GetSpendingForecastQueryHandler : IGetSpendingForecastQueryHandler
{
    private readonly ISpendingForecaster _forecaster;
    private readonly IUserRepository _userRepository;

    public GetSpendingForecastQueryHandler(
        ISpendingForecaster forecaster, IUserRepository userRepository)
    {
        _forecaster = forecaster;
        _userRepository = userRepository;
    }

    public async Task<SpendingForecastDto> HandleAsync(
        GetSpendingForecastQuery query, CancellationToken ct = default)
    {
        var forecast = await _forecaster.ForecastCurrentMonthAsync(query.UserId, ct);

        var user = await _userRepository.GetByIdAsync(query.UserId, ct)
            ?? throw new NotFoundException("User", query.UserId);

        return forecast with { Advice = BuildAdvice(forecast, user.MonthlyIncomeCents) };
    }

    /// <summary>
    /// Phần so-với-thu-nhập tính ở ĐÂY, không nhét vào <see cref="ISpendingForecaster"/>.
    /// Forecaster chỉ làm một việc: ngoại suy tốc độ tiêu tiền. Giữ nó thuần thống kê là giữ
    /// nguyên chỗ cắm model AI sau này mà không kéo theo nghiệp vụ thu nhập.
    /// </summary>
    private static ForecastAdviceDto? BuildAdvice(SpendingForecastDto forecast, long? monthlyIncomeCents)
    {
        if (monthlyIncomeCents is not { } income || income <= 0)
        {
            return null;
        }

        var balance = income - forecast.ProjectedSpendCents;
        if (balance >= 0)
        {
            return new ForecastAdviceDto(income, balance, null);
        }

        // Ngày cuối tháng thì không còn ngày nào để cắt — dồn cả phần bội chi vào hôm nay,
        // cùng cách GetGoalProgressQueryHandler xử lý mục tiêu đã quá hạn.
        var remainingDays = Math.Max(1, forecast.DaysInMonth - forecast.DaysElapsed);
        var dailyCut = (long)Math.Ceiling(-balance / (double)remainingDays);

        return new ForecastAdviceDto(income, balance, dailyCut);
    }
}
