using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.API.Middleware;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Controllers;

/// <summary>API cho website thanh toán. Ngoài bước đổi mã, mọi API cần phiên thanh toán, không nhận JWT. Hợp đồng ở <c>docs/subscription-api.md</c>.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.BillingSession)]
[Route("api/v1/billing/web")]
public class BillingWebController : ControllerBase
{
    private readonly ISubscriptionCheckoutService _checkout;
    private readonly ISubscriptionEntitlementService _entitlements;

    public BillingWebController(ISubscriptionCheckoutService checkout, ISubscriptionEntitlementService entitlements)
    {
        _checkout = checkout;
        _entitlements = entitlements;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingMiddleware.AuthPolicy)]
    [HttpPost("session")]
    public async Task<IActionResult> Session([FromBody] ExchangeBillingCodeRequest request, CancellationToken ct)
        => Ok(ApiResponse<BillingSessionDto>.Ok(await _checkout.ExchangeCodeAsync(request.Code, ct)));

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PublicPlanDto>>.Ok(await _checkout.ListPlansAsync(ct)));

    /// <summary>Gói hiện tại và ngày kỳ mua thêm sẽ bắt đầu. Web phải cho khách thấy ngày này trước khi trả, vì không có nâng cấp tức thì.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
        => Ok(ApiResponse<MySubscriptionDto>.Ok(await _entitlements.GetMineAsync(CurrentUserId, ct)));

    /// <summary>Tạo đơn và trả form đã ký. Trình duyệt tự POST form này sang SePay; ký ở backend vì secret không được rời máy chủ.</summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken ct)
        => Ok(ApiResponse<CheckoutFormDto>.Ok(await _checkout.CheckoutAsync(CurrentUserId, request.PlanId, request.Cycle!.Value, ct)));

    [HttpGet("orders/{invoiceNumber}")]
    public async Task<IActionResult> Order(string invoiceNumber, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionOrderDto>.Ok(await _checkout.GetOrderAsync(CurrentUserId, invoiceNumber, ct)));
}

public record ExchangeBillingCodeRequest(string Code);

/// <param name="Cycle">Bắt buộc và phải đúng giá trị: mặc định âm thầm thành tháng thì khách trả nhầm chu kỳ.</param>
public record CheckoutRequest(Guid PlanId, [Required, EnumDataType(typeof(SubscriptionCycle))] SubscriptionCycle? Cycle);
