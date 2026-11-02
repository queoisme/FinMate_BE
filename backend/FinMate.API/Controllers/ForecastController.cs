using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Dự báo tài chính (Flow 6, đợt 1): dòng tiền theo ngày, thử kịch bản, số tiền có thể chi. Mọi số liệu theo người dùng trong JWT.</summary>
[ApiController]
[Route("api/v1/forecast")]
[Authorize]
public class ForecastController : ControllerBase
{
    private readonly IForecastService _forecast;

    public ForecastController(IForecastService forecast)
    {
        _forecast = forecast;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Số dư dự kiến từng ngày từ hôm nay tới <paramref name="to"/> (mặc định cuối tháng). Có <paramref name="accountId"/> thì chỉ xem ví đó.</summary>
    [HttpGet("cashflow")]
    public async Task<IActionResult> Cashflow([FromQuery] Guid? accountId, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(ApiResponse<CashflowForecastDto>.Ok(await _forecast.GetCashflowAsync(CurrentUserId, accountId, to, ct)));

    /// <summary>Tính lại số dư với các khoản thu/chi giả định. Không lưu gì.</summary>
    [HttpPost("cashflow/scenario")]
    public async Task<IActionResult> Scenario([FromBody] CashflowScenarioRequest request, CancellationToken ct)
    {
        var items = (request.Items ?? []).Select(i => new ScenarioItem(i.Date, i.AmountCents, i.Direction, i.Label ?? string.Empty)).ToList();
        var result = await _forecast.RunScenarioAsync(new CashflowScenarioCommand(CurrentUserId, request.AccountId, request.To, items), ct);
        return Ok(ApiResponse<CashflowForecastDto>.Ok(result));
    }

    /// <summary>Số tiền có thể chi trong kỳ và mỗi ngày, kèm bảng cách tính.</summary>
    [HttpGet("spendable")]
    public async Task<IActionResult> Spendable(CancellationToken ct)
        => Ok(ApiResponse<SafeToSpendDto>.Ok(await _forecast.GetSpendableAsync(CurrentUserId, ct)));
}

public record ScenarioItemRequest(DateOnly Date, long AmountCents, RecurringDirection Direction, string? Label);

public record CashflowScenarioRequest(Guid? AccountId, DateOnly? To, IReadOnlyList<ScenarioItemRequest>? Items);
