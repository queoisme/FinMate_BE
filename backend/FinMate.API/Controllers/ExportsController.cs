using System.Security.Claims;
using System.Text;
using FinMate.Application.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Xuất dữ liệu của chính người dùng. Hiện có CSV giao dịch đã xác nhận.</summary>
[ApiController]
[Route("api/v1/exports")]
[Authorize]
public class ExportsController : ControllerBase
{
    private readonly ITransactionExportService _export;

    public ExportsController(ITransactionExportService export) => _export = export;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// CSV giao dịch đã xác nhận từ <paramref name="from"/> tới <paramref name="to"/> (gồm cả hai ngày, giờ Việt Nam).
    /// <paramref name="delimiter"/>: <c>comma</c> (mặc định) hoặc <c>semicolon</c> cho Excel đặt vùng Việt Nam.
    /// </summary>
    [HttpGet("transactions.csv")]
    public async Task<IActionResult> TransactionsCsv(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? accountId, [FromQuery] string? delimiter, CancellationToken ct)
    {
        var csv = await _export.ExportCsvAsync(CurrentUserId, from, to, accountId, delimiter, ct);
        Response.Headers.CacheControl = "no-store";   // dữ liệu tài chính: không để proxy/trình duyệt giữ lại
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"finmate-giao-dich-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }
}
