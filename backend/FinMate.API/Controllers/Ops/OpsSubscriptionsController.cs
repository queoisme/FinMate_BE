using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>Cấu hình gói đăng ký. Policy chỉ chặn sớm; quyền thật được đọc lại từ DB trong service. Chạy cả khi cờ tắt, để chuẩn bị gói trước khi bật.</summary>
[Route("api/v1/ops/subscriptions")]
[Authorize(Policy = AuthorizationPolicies.ManagerOrAbove)]
public class OpsSubscriptionsController : OpsControllerBase
{
    private readonly IOpsSubscriptionService _subscriptions;

    public OpsSubscriptionsController(IOpsSubscriptionService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    [HttpGet("features")]
    public async Task<IActionResult> Features(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SubscriptionFeatureDefinition>>.Ok(await _subscriptions.ListFeaturesAsync(CurrentActorId, ct)));

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<SubscriptionPlanDto>>.Ok(await _subscriptions.ListPlansAsync(CurrentActorId, ct)));

    [HttpPost("plans")]
    public async Task<IActionResult> Create([FromBody] CreatePlanInput request, CancellationToken ct)
    {
        var plan = await _subscriptions.CreatePlanAsync(CurrentActorId, request, CurrentIpAddress, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<SubscriptionPlanDto>.Ok(plan));
    }

    [HttpPatch("plans/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlanInput request, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionPlanDto>.Ok(await _subscriptions.UpdatePlanAsync(CurrentActorId, id, request, CurrentIpAddress, ct)));

    /// <summary>Ngừng bán, không xóa vật lý. Người đang dùng gói giữ nguyên đến hết kỳ.</summary>
    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _subscriptions.DeletePlanAsync(CurrentActorId, id, CurrentIpAddress, ct);
        return NoContent();
    }

    /// <summary>Thay toàn bộ chức năng của gói. Chức năng không gửi lên là không có trong gói.</summary>
    [HttpPut("plans/{id:guid}/features")]
    public async Task<IActionResult> SetFeatures(Guid id, [FromBody] IReadOnlyList<PlanFeatureInput> request, CancellationToken ct)
        => Ok(ApiResponse<SubscriptionPlanDto>.Ok(await _subscriptions.SetFeaturesAsync(CurrentActorId, id, request, CurrentIpAddress, ct)));

    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] SubscriptionOrderStatus? status, [FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(ApiResponse<OpsSubscriptionOrderPageDto>.Ok(await _subscriptions.ListOrdersAsync(CurrentActorId, status, offset, limit, ct)));
}
