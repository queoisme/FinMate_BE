using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Economics;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Số liệu kinh tế tự cập nhật. Lịch và địa chỉ nguồn nằm ở cấu hình hệ thống (nhóm <c>economicFeeds</c>); controller này chỉ có nút "Chạy ngay" để thử sau khi đổi cấu hình.</summary>
[Route("api/v1/admin/economic-feeds")]
public class AdminEconomicFeedsController(EconomicAssumptionRefresher refresher, IAuditLogService audit) : AdminControllerBase
{
    public sealed record RefreshResult(string Outcome);

    /// <summary>
    /// Chạy ngay một lượt làm mới (đồng bộ, vài giây). Kết quả: <c>Disabled</c> (chưa đặt địa chỉ nguồn), <c>AlreadyCurrent</c>, <c>AutoApproved</c>, <c>NeedsReview</c> (để Draft chờ duyệt) hoặc <c>Failed</c>.
    /// Không phụ thuộc công tắc <c>enabled</c> của lịch. Chỉ Admin; để lại một dòng audit.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var outcome = await refresher.RunAsync(ct);
        await audit.LogAsync(AuditEvents.AdminEconomicFeedRefreshed, CurrentAdminId, CurrentIpAddress, new { Outcome = outcome.ToString() }, ct);
        return Ok(ApiResponse<RefreshResult>.Ok(new RefreshResult(outcome.ToString())));
    }
}
