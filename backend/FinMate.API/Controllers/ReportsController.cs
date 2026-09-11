using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Reports.Commands;
using FinMate.Application.Reports.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private const int DefaultTimelineLimit = 20;
    private const int MaxTimelineLimit = 100;
    private const int DefaultInsightLimit = 20;

    private readonly IGetMonthlySummaryQueryHandler _monthlySummaryHandler;
    private readonly IGetCategoryBreakdownQueryHandler _breakdownHandler;
    private readonly IGetTransactionTimelineQueryHandler _timelineHandler;
    private readonly IGetSpendingForecastQueryHandler _forecastHandler;
    private readonly IGetSpendingInsightsQueryHandler _insightsHandler;
    private readonly IMarkInsightReadCommandHandler _markReadHandler;

    public ReportsController(
        IGetMonthlySummaryQueryHandler monthlySummaryHandler,
        IGetCategoryBreakdownQueryHandler breakdownHandler,
        IGetTransactionTimelineQueryHandler timelineHandler,
        IGetSpendingForecastQueryHandler forecastHandler,
        IGetSpendingInsightsQueryHandler insightsHandler,
        IMarkInsightReadCommandHandler markReadHandler)
    {
        _monthlySummaryHandler = monthlySummaryHandler;
        _breakdownHandler = breakdownHandler;
        _timelineHandler = timelineHandler;
        _forecastHandler = forecastHandler;
        _insightsHandler = insightsHandler;
        _markReadHandler = markReadHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("monthly-summary")]
    public async Task<IActionResult> GetMonthlySummary([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var summary = await _monthlySummaryHandler.HandleAsync(
            new GetMonthlySummaryQuery(CurrentUserId, year, month), ct);
        return Ok(ApiResponse<MonthlySummaryDto>.Ok(summary));
    }

    [HttpGet("category-breakdown")]
    public async Task<IActionResult> GetCategoryBreakdown([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var breakdown = await _breakdownHandler.HandleAsync(
            new GetCategoryBreakdownQuery(CurrentUserId, year, month), ct);
        return Ok(ApiResponse<CategoryBreakdownDto>.Ok(breakdown));
    }

    [HttpGet("timeline")]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? cursor,
        [FromQuery] int limit = DefaultTimelineLimit,
        CancellationToken ct = default)
    {
        var timeline = await _timelineHandler.HandleAsync(
            new GetTransactionTimelineQuery(
                CurrentUserId, fromDate, toDate, cursor, Math.Clamp(limit, 1, MaxTimelineLimit)),
            ct);

        return Ok(ApiResponse<IReadOnlyList<TimelineDayDto>>.Ok(timeline.Days, new ApiMeta(timeline.NextCursor)));
    }

    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast(CancellationToken ct)
    {
        var forecast = await _forecastHandler.HandleAsync(new GetSpendingForecastQuery(CurrentUserId), ct);
        return Ok(ApiResponse<SpendingForecastDto>.Ok(forecast));
    }

    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights([FromQuery] bool unreadOnly = false, CancellationToken ct = default)
    {
        var insights = await _insightsHandler.HandleAsync(
            new GetSpendingInsightsQuery(CurrentUserId, unreadOnly, DefaultInsightLimit), ct);
        return Ok(ApiResponse<IReadOnlyList<SpendingInsightDto>>.Ok(insights));
    }

    [HttpPatch("insights/{id:guid}/read")]
    public async Task<IActionResult> MarkInsightRead(Guid id, CancellationToken ct)
    {
        await _markReadHandler.HandleAsync(new MarkInsightReadCommand(CurrentUserId, id), ct);
        return NoContent();
    }
}
