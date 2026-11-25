using System.Security.Claims;
using FinMate.Application.DataExport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Xuất dữ liệu (Phase 91, <c>docs/data-export-api.md</c>). Không gắn <c>[RequireSubscriptionFeature]</c>: quyền lấy dữ liệu của mình là quyền theo luật,
/// không phải tính năng bán (BR-EXP-13). Không log nội dung file, số tiền hay link tải.
/// </summary>
[ApiController]
[Route("api/v1/exports")]
[Authorize]
public class ExportsController(TransactionCsvExportService csv) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// BR-EXP-01: CSV giao dịch, trả thẳng file. <paramref name="from"/>/<paramref name="to"/> là ngày giờ VN (gồm cả hai đầu), mặc định tháng này.
    /// Mọi lỗi (400/404/422/429) trả JSON trước khi gửi byte nào của file.
    /// </summary>
    [HttpGet("transactions.csv")]
    public async Task TransactionsCsv(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId, [FromQuery] Guid? categoryId,
        [FromQuery] string? status, [FromQuery] string? delimiter, CancellationToken ct)
    {
        var plan = await csv.PrepareAsync(CurrentUserId, new TransactionCsvRequest(from, to, accountId, categoryId, status, delimiter), RemoteIp, ct);

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{plan.FileName}\"";
        Response.Headers.CacheControl = "no-store";
        await csv.WriteAsync(CurrentUserId, plan, Response.Body, ct);
    }
}
