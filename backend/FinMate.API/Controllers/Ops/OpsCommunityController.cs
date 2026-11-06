using FinMate.Application.Common.Models;
using FinMate.Application.Community;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>Kiểm duyệt cộng đồng. Không có response nào chứa người đăng hay người báo cáo. Chạy được cả khi cờ tắt, để xử lý nội dung còn tồn.</summary>
[Route("api/v1/ops/community")]
public class OpsCommunityController : OpsControllerBase
{
    private readonly IOpsCommunityService _community;

    public OpsCommunityController(IOpsCommunityService community)
    {
        _community = community;
    }

    /// <summary>Báo cáo còn mở, khẩn trước, cũ trước.</summary>
    [HttpGet("reports")]
    public async Task<IActionResult> Reports([FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(ApiResponse<OpsCommunityReportPageDto>.Ok(await _community.ListReportsAsync(CurrentActorId, offset, limit, ct)));

    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] OpsResolveReportInput request, CancellationToken ct)
    {
        await _community.ResolveReportAsync(CurrentActorId, id, request, CurrentIpAddress, ct);
        return NoContent();
    }

    /// <summary>Ảnh chờ duyệt, cũ trước.</summary>
    [HttpGet("images")]
    public async Task<IActionResult> Images([FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(ApiResponse<OpsCommunityImagePageDto>.Ok(await _community.ListPendingImagesAsync(CurrentActorId, offset, limit, ct)));

    [HttpGet("images/{id:guid}")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var content = await _community.GetImageAsync(CurrentActorId, id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(content.Data, "image/jpeg");
    }

    [HttpPost("images/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _community.ApproveImageAsync(CurrentActorId, id, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("images/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        await _community.RejectImageAsync(CurrentActorId, id, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("posts/{id:guid}/hide")]
    public async Task<IActionResult> HidePost(Guid id, CancellationToken ct)
    {
        await _community.SetPostVisibleAsync(CurrentActorId, id, false, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("posts/{id:guid}/restore")]
    public async Task<IActionResult> RestorePost(Guid id, CancellationToken ct)
    {
        await _community.SetPostVisibleAsync(CurrentActorId, id, true, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("comments/{id:guid}/hide")]
    public async Task<IActionResult> HideComment(Guid id, CancellationToken ct)
    {
        await _community.SetCommentVisibleAsync(CurrentActorId, id, false, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("comments/{id:guid}/restore")]
    public async Task<IActionResult> RestoreComment(Guid id, CancellationToken ct)
    {
        await _community.SetCommentVisibleAsync(CurrentActorId, id, true, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("places/{id:guid}/hide")]
    public async Task<IActionResult> HidePlace(Guid id, CancellationToken ct)
    {
        await _community.HidePlaceAsync(CurrentActorId, id, CurrentIpAddress, ct);
        return NoContent();
    }

    /// <summary>Chỉ từ OpsManager. Cấm theo nội dung, người xử lý không biết tác giả là ai.</summary>
    [HttpPost("posts/{id:guid}/mute-author")]
    public async Task<IActionResult> MutePostAuthor(Guid id, [FromBody] OpsMuteAuthorInput request, CancellationToken ct)
    {
        await _community.MutePostAuthorAsync(CurrentActorId, id, request, CurrentIpAddress, ct);
        return NoContent();
    }

    [HttpPost("comments/{id:guid}/mute-author")]
    public async Task<IActionResult> MuteCommentAuthor(Guid id, [FromBody] OpsMuteAuthorInput request, CancellationToken ct)
    {
        await _community.MuteCommentAuthorAsync(CurrentActorId, id, request, CurrentIpAddress, ct);
        return NoContent();
    }
}
