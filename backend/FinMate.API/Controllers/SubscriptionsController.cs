using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Gói đăng ký phía app. Việc mua diễn ra trên website: app chỉ xin liên kết và mở bằng trình duyệt.</summary>
[ApiController]
[Authorize]
[Route("api/v1/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionCheckoutService _checkout;
    private readonly ISubscriptionEntitlementService _entitlements;

    public SubscriptionsController(ISubscriptionCheckoutService checkout, ISubscriptionEntitlementService entitlements)
    {
        _checkout = checkout;
        _entitlements = entitlements;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PublicPlanDto>>.Ok(await _checkout.ListPlansAsync(ct)));

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
        => Ok(ApiResponse<MySubscriptionDto>.Ok(await _entitlements.GetMineAsync(CurrentUserId, ct)));

    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string? cursor, [FromQuery] int limit, CancellationToken ct = default)
    {
        var result = await _checkout.ListMyOrdersAsync(CurrentUserId, cursor, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<SubscriptionOrderDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

    /// <summary>Liên kết sang website, mang mã dùng một lần, không mang token đăng nhập.</summary>
    [HttpPost("web-session")]
    public async Task<IActionResult> WebSession([FromBody] CreateWebSessionRequest? request, CancellationToken ct)
        => Ok(ApiResponse<WebSessionLinkDto>.Ok(await _checkout.CreateWebSessionAsync(CurrentUserId, request?.PlanId, ct)));
}

public record CreateWebSessionRequest(Guid? PlanId);
