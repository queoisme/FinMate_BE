using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// "Token và chi phí AI" cho trang admin: token của trợ lý tài chính trong <c>days</c> ngày gần nhất (1–366, mặc định 30), tổng, theo bước (gate/plan/compose), và theo ngày,
/// kèm chi phí ước tính theo giá trong cấu hình hệ thống (xem <see cref="IAdminAgentUsageService"/>). Chỉ đọc; không có băm người dùng hay nội dung câu hỏi trong dữ liệu trả về.
/// </summary>
[Route("api/v1/admin/agent-usage")]
public class AdminAgentUsageController(IAdminAgentUsageService service) : AdminControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await service.SummaryAsync(
            Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString()), ct)));
}
