using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Khoản thu/chi định kỳ (lương, tiền nhà, thuê bao): xem, thêm tay, sửa, và quyết định với các đề xuất hệ thống phát hiện.</summary>
[ApiController]
[Route("api/v1/recurring-items")]
[Authorize]
public class RecurringItemsController : ControllerBase
{
    private readonly IRecurringItemService _service;

    public RecurringItemsController(IRecurringItemService service)
    {
        _service = service;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Không truyền <paramref name="status"/> thì trả mọi khoản trừ đã hủy và đã từ chối.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RecurringStatus? status, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<RecurringItemDto>>.Ok(await _service.ListAsync(CurrentUserId, status, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringItemRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(
            new CreateRecurringItemCommand(
                CurrentUserId, request.Direction, request.Name ?? string.Empty, request.CounterpartyName, request.AmountCents,
                request.Cadence, request.NextDueDate, request.CategoryId, request.FinancialAccountId),
            ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<RecurringItemDto>.Ok(created));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRecurringItemRequest request, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.UpdateAsync(
            new UpdateRecurringItemCommand(
                CurrentUserId, id, request.Name, request.AmountCents, request.Cadence, request.NextDueDate, request.CategoryId, request.FinancialAccountId),
            ct)));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.ConfirmAsync(CurrentUserId, id, ct)));

    /// <summary>Từ chối đề xuất; hệ thống sẽ không đề xuất lại cùng người nhận.</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.RejectAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.PauseAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecurringItemDto>.Ok(await _service.ResumeAsync(CurrentUserId, id, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await _service.CancelAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}

public record CreateRecurringItemRequest(
    RecurringDirection Direction,
    string? Name,
    string? CounterpartyName,
    long AmountCents,
    RecurringCadence Cadence,
    DateOnly NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId);

public record UpdateRecurringItemRequest(
    string? Name,
    long? AmountCents,
    RecurringCadence? Cadence,
    DateOnly? NextDueDate,
    Guid? CategoryId,
    Guid? FinancialAccountId);
