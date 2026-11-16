using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

/// <summary>
/// Chất lượng dự báo cho nhân sự: chỉ số độ chính xác (OpsManager trở lên, quyền xem giám sát) và hàng đợi phản hồi "dự báo sai" (Staff trở lên).
/// Chính sách StaffOrAbove nằm ở lớp cha; quyền cụ thể do dịch vụ kiểm tra từ DB. Không có định danh người dùng ở bất kỳ đâu.
/// </summary>
[Route("api/v1/ops/forecast")]
public class OpsForecastController(IForecastAccuracyService accuracy, IForecastFeedbackService feedback) : OpsControllerBase
{
    public sealed record LabelRequest(string Cause);

    /// <summary><c>windowDays</c> 7–180 (mặc định 30): sai số số dư theo mốc 7/14/30 ngày, độ phủ dải, so với hai mốc B0 và "số dư không đổi", tách nhóm ít dữ liệu.</summary>
    [HttpGet("accuracy")]
    public async Task<IActionResult> Accuracy([FromQuery] int windowDays = 30, CancellationToken ct = default)
        => Ok(ApiResponse<ForecastAccuracyReportDto>.Ok(await accuracy.GetReportAsync(CurrentActorId, windowDays, ct)));

    /// <summary><c>status</c>: <c>new</c> hoặc <c>labeled</c>; bỏ trống là tất cả. Phân trang <c>limit</c> (1–100) và <c>offset</c>.</summary>
    [HttpGet("feedback")]
    public async Task<IActionResult> Feedback([FromQuery] string? status, [FromQuery] int limit = 20, [FromQuery] int offset = 0, CancellationToken ct = default)
        => Ok(ApiResponse<ForecastFeedbackPageDto>.Ok(await feedback.ListAsync(CurrentActorId, status, limit, offset, ct)));

    /// <summary>Gán nguyên nhân: <c>data_missing</c>, <c>recurring_misread</c>, <c>one_off</c>, <c>model_issue</c>, <c>other</c>. 404 nếu không có phản hồi.</summary>
    [HttpPatch("feedback/{id:guid}")]
    public async Task<IActionResult> Label(Guid id, [FromBody] LabelRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastFeedbackDto>.Ok(await feedback.LabelAsync(CurrentActorId, id, request.Cause, CurrentIpAddress, ct)));
}
