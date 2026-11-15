using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Support;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Khách gửi và theo dõi yêu cầu hỗ trợ trong app.</summary>
[ApiController]
[Route("api/v1/support/tickets")]
[Authorize]
public class SupportTicketsController : ControllerBase
{
    private readonly ISupportTicketService _tickets;

    public SupportTicketsController(ISupportTicketService tickets)
    {
        _tickets = tickets;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request, CancellationToken ct)
    {
        var detail = await _tickets.CreateAsync(CurrentUserId, request.Category, request.Subject, request.Body, ct);
        return CreatedAtAction(nameof(Get), new { id = detail.Ticket.Id }, ApiResponse<SupportTicketDetailDto>.Ok(detail));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? cursor, [FromQuery] int limit, CancellationToken ct = default)
    {
        var result = await _tickets.ListAsync(CurrentUserId, cursor, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<SupportTicketDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDetailDto>.Ok(await _tickets.GetAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] SupportMessageRequest request, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDetailDto>.Ok(await _tickets.ReplyAsync(CurrentUserId, id, request.Body, ct)));

    [HttpPost("{id:guid}/messages/{messageId:guid}/attachments")]
    [RequestSizeLimit(SupportTicketRules.MaxAttachmentBytes + 4096)]
    public async Task<IActionResult> AddAttachment(Guid id, Guid messageId, IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var attachment = await _tickets.AddAttachmentAsync(CurrentUserId, id, messageId, buffer.ToArray(), ct);
        return Ok(ApiResponse<SupportAttachmentDto>.Ok(attachment));
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> GetAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var content = await _tickets.GetAttachmentAsync(CurrentUserId, id, attachmentId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Data, content.ContentType);
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await _tickets.CloseAsync(CurrentUserId, id, ct)));

    [HttpPost("{id:guid}/rating")]
    public async Task<IActionResult> Rate(Guid id, [FromBody] SupportRatingRequest request, CancellationToken ct)
        => Ok(ApiResponse<SupportTicketDto>.Ok(await _tickets.RateAsync(CurrentUserId, id, request.Rating, ct)));
}

public record CreateSupportTicketRequest(SupportTicketCategory Category, string? Subject, string? Body);
public record SupportMessageRequest(string? Body);
public record SupportRatingRequest(int Rating);
