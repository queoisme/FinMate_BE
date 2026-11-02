using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Dữ liệu đầu vào của dự báo mà user chỉnh được: khoản dự phòng, ví nào được tính, giao dịch nào loại khỏi thói quen chi tiêu.
/// Gom một chỗ thay vì nhét vào TransactionsController/FinancialAccountsController; đường dẫn vẫn nằm dưới tài nguyên tương ứng.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class ForecastSettingsController : ControllerBase
{
    private readonly IForecastSettingsService _settings;

    public ForecastSettingsController(IForecastSettingsService settings)
    {
        _settings = settings;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me/forecast-settings")]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<ForecastSettingsDto>.Ok(await _settings.GetAsync(CurrentUserId, ct)));

    /// <summary>
    /// Thay toàn bộ cài đặt dự báo. <c>reserveCents: null</c> = dự phòng mặc định; <c>lowBalanceThresholdCents: null</c> = tắt cảnh báo số dư thấp;
    /// <c>quietHoursStart/End</c> "HH:mm" hoặc cùng null = giờ yên lặng mặc định.
    /// </summary>
    [HttpPut("me/forecast-settings")]
    public async Task<IActionResult> Put([FromBody] ForecastSettingsRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastSettingsDto>.Ok(await _settings.SetAsync(
            new UpdateForecastSettingsCommand(
                CurrentUserId, request.ReserveCents, request.LowBalanceThresholdCents, request.QuietHoursStart, request.QuietHoursEnd,
                request.ForecastAlertsEnabled ?? true),
            ct)));

    [HttpPatch("financial-accounts/{id:guid}/forecast-inclusion")]
    public async Task<IActionResult> SetAccountInclusion(Guid id, [FromBody] ForecastInclusionRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastAccountDto>.Ok(await _settings.SetAccountInclusionAsync(CurrentUserId, id, request.Included, ct)));

    /// <summary>Loại giao dịch khỏi dữ liệu dự báo (vd khoản chi một lần). Số dư không đổi.</summary>
    [HttpPatch("transactions/{id:guid}/forecast-exclusion")]
    public async Task<IActionResult> SetTransactionExclusion(Guid id, [FromBody] ForecastExclusionRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransactionForecastExclusionDto>.Ok(
            await _settings.SetTransactionExclusionAsync(CurrentUserId, id, request.Excluded, request.Reason, ct)));
}

public record ForecastSettingsRequest(
    long? ReserveCents, long? LowBalanceThresholdCents, string? QuietHoursStart, string? QuietHoursEnd, bool? ForecastAlertsEnabled);

public record ForecastInclusionRequest(bool Included);

public record ForecastExclusionRequest(bool Excluded, ForecastExclusionReason? Reason);
