using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

/// <summary>Khoảng ngày theo giờ Việt Nam, gồm cả hai đầu. <paramref name="AccountId"/> null = mọi ví.</summary>
public record GetPeriodSummaryQuery(Guid UserId, DateOnly From, DateOnly To, Guid? AccountId = null);

/// <param name="PrevFrom">Kỳ liền trước dài bằng kỳ này (kết thúc ngày hôm trước <paramref name="From"/>): để so sánh ở tab Ngày/Tuần/Năm.</param>
/// <param name="SpentChangePercent">% đổi của tổng chi so với kỳ trước; null khi kỳ trước không có chi.</param>
/// <param name="IncomeChangePercent">% đổi của tổng thu so với kỳ trước; null khi kỳ trước không có thu.</param>
public record PeriodSummaryDto(
    DateOnly From, DateOnly To, long TotalSpentCents, long TotalIncomeCents, long NetCents, int TransactionCount,
    DateOnly PrevFrom, DateOnly PrevTo, long PrevTotalSpentCents, long PrevTotalIncomeCents,
    double? SpentChangePercent, double? IncomeChangePercent);

public interface IGetPeriodSummaryQueryHandler
{
    Task<PeriodSummaryDto> HandleAsync(GetPeriodSummaryQuery query, CancellationToken ct = default);
}

/// <summary>
/// Tổng chi / tổng thu cho một khoảng ngày bất kỳ (màn lịch sử giao dịch chọn "từ ngày … đến ngày …", kể cả năm trước).
/// Cùng nguồn với <c>monthly-summary</c> và <c>timeline</c> (<c>GetDailySpendAsync</c>) nên ba màn luôn cho cùng một con số.
/// </summary>
public class GetPeriodSummaryQueryHandler : IGetPeriodSummaryQueryHandler
{
    /// <summary>Chặn khoảng quá dài để một lần gọi không quét cả lịch sử: truy vấn luôn thuộc MỘT user và đi qua index (user_id, transacted_at).</summary>
    public const int MaxDays = 1100;

    private readonly IReportRepository _reportRepository;

    public GetPeriodSummaryQueryHandler(IReportRepository reportRepository) => _reportRepository = reportRepository;

    private static double? Change(long current, long previous)
        => previous > 0 ? Math.Round((current - previous) * 100.0 / previous, 1) : null;

    public async Task<PeriodSummaryDto> HandleAsync(GetPeriodSummaryQuery query, CancellationToken ct = default)
    {
        if (query.From > query.To)
        {
            throw new BusinessRuleException(ReportErrorCodes.InvalidRange, "Ngày bắt đầu phải trước hoặc cùng ngày kết thúc.");
        }

        if (query.To.DayNumber - query.From.DayNumber + 1 > MaxDays)
        {
            throw new BusinessRuleException(ReportErrorCodes.RangeTooLong, $"Khoảng thời gian tối đa {MaxDays} ngày (khoảng 3 năm).");
        }

        var length = query.To.DayNumber - query.From.DayNumber + 1;
        var prevTo = query.From.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(length - 1));

        var points = await _reportRepository.GetDailySpendAsync(query.UserId, query.From, query.To, ct, query.AccountId);
        var prevPoints = await _reportRepository.GetDailySpendAsync(query.UserId, prevFrom, prevTo, ct, query.AccountId);
        var spent = points.Sum(p => p.SpentCents);
        var income = points.Sum(p => p.IncomeCents);
        var prevSpent = prevPoints.Sum(p => p.SpentCents);
        var prevIncome = prevPoints.Sum(p => p.IncomeCents);
        return new PeriodSummaryDto(
            query.From, query.To, spent, income, income - spent, points.Sum(p => p.TransactionCount),
            prevFrom, prevTo, prevSpent, prevIncome, Change(spent, prevSpent), Change(income, prevIncome));
    }
}
