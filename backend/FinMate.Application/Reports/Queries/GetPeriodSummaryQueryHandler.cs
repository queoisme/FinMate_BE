using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Reports.Queries;

/// <summary>Khoảng ngày theo giờ Việt Nam, gồm cả hai đầu. <paramref name="AccountId"/> null = mọi ví.</summary>
public record GetPeriodSummaryQuery(Guid UserId, DateOnly From, DateOnly To, Guid? AccountId = null);

public record PeriodSummaryDto(
    DateOnly From, DateOnly To, long TotalSpentCents, long TotalIncomeCents, long NetCents, int TransactionCount);

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

        var points = await _reportRepository.GetDailySpendAsync(query.UserId, query.From, query.To, ct, query.AccountId);
        var spent = points.Sum(p => p.SpentCents);
        var income = points.Sum(p => p.IncomeCents);
        return new PeriodSummaryDto(query.From, query.To, spent, income, income - spent, points.Sum(p => p.TransactionCount));
    }
}
