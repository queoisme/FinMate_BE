using System.Security.Claims;
using FinMate.Application.Common.Models;
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
public class ExportsController(TransactionCsvExportService csv, PersonalDataExportService personalData, PersonalDataDownloadService downloads) : ControllerBase
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
        [FromQuery] string? status, [FromQuery] string? delimiter, [FromQuery] bool? includeDeleted, CancellationToken ct)
    {
        var plan = await csv.PrepareAsync(CurrentUserId, new TransactionCsvRequest(from, to, accountId, categoryId, status, delimiter, includeDeleted ?? false), RemoteIp, ct);

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{plan.FileName}\"";
        Response.Headers.CacheControl = "no-store";
        await csv.WriteAsync(CurrentUserId, plan, Response.Body, ct);
    }

    /// <summary>BR-EXP-06: gửi mã xác nhận tới email. Hạn mức được kiểm trước (429 thì không gửi mail).</summary>
    [HttpPost("personal-data/otp")]
    public async Task<IActionResult> SendPersonalDataOtp(CancellationToken ct)
    {
        await personalData.SendOtpAsync(CurrentUserId, ct);
        return Accepted();
    }

    /// <summary>BR-EXP-06: tạo yêu cầu gói đầy đủ. Thiếu hoặc sai mã → 403; đang có yêu cầu chạy hoặc đã có trong 24 giờ → 429.</summary>
    [HttpPost("personal-data")]
    public async Task<IActionResult> RequestPersonalData([FromBody] PersonalDataExportRequest? request, CancellationToken ct)
        => Accepted(ApiResponse<PersonalDataExportDto>.Ok(await personalData.RequestAsync(CurrentUserId, request?.Code, RemoteIp, ct)));

    /// <summary>BR-EXP-11: các yêu cầu của chính user trong cửa sổ lịch sử, mới nhất trước. Không phân trang (tối đa vài trăm dòng).</summary>
    [HttpGet("personal-data")]
    public async Task<IActionResult> ListPersonalData(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PersonalDataExportDto>>.Ok(await personalData.ListAsync(CurrentUserId, ct)));

    /// <summary>BR-EXP-10: trạng thái một yêu cầu. Id của người khác → 404.</summary>
    [HttpGet("personal-data/{id:guid}")]
    public async Task<IActionResult> GetPersonalData(Guid id, CancellationToken ct)
        => Ok(ApiResponse<PersonalDataExportDto>.Ok(await personalData.GetAsync(CurrentUserId, id, ct)));

    /// <summary>BR-EXP-10: link tải dùng một lần, sống <c>downloadLinkMinutes</c>. Chưa xong → 409; file đã hết hạn → 410. Link mới thay link cũ.</summary>
    [HttpPost("personal-data/{id:guid}/download-link")]
    public async Task<IActionResult> IssueDownloadLink(Guid id, CancellationToken ct)
        => Ok(ApiResponse<PersonalDataDownloadLinkDto>.Ok(await downloads.IssueLinkAsync(CurrentUserId, id, RemoteIp, ct)));

    /// <summary>
    /// BR-EXP-10, D7, D9: tải ZIP. Vẫn đòi đăng nhập đúng user; token ở query (log request chỉ ghi đường dẫn). Link chết ngay khi bắt đầu tải; dùng lại,
    /// quá hạn hay bị link mới thay → 410. Id của người khác → 404.
    /// </summary>
    [HttpGet("personal-data/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] string? token, CancellationToken ct)
    {
        var file = await downloads.DownloadAsync(CurrentUserId, id, token, RemoteIp, ct);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, "application/zip", file.FileName);
    }
}

public record PersonalDataExportRequest(string? Code);
