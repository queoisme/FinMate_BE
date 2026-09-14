using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Giám sát hàng đợi xoá tài khoản.
///
/// <c>DataDeletionJob</c> xoá cứng bằng <c>ExecuteDeleteAsync</c> lúc 03:00 mỗi ngày, không
/// hoàn tác được. Trước hai endpoint này, hàng đợi đó chạy mà không ai nhìn thấy và không có
/// cách nào dừng lại.
///
/// **Không có endpoint xoá ngay.** Giám sát nghĩa là nhìn thấy và ngăn được; một nút xoá tức
/// thì chỉ thêm một đường phá huỷ dữ liệu mà bấm nhầm một lần là không cứu được.
/// </summary>
[Route("api/v1/admin/data-deletion-requests")]
public class AdminDataDeletionController : AdminControllerBase
{
    private readonly IGetDataDeletionRequestListQueryHandler _listHandler;
    private readonly ICancelDataDeletionRequestCommandHandler _cancelHandler;

    public AdminDataDeletionController(
        IGetDataDeletionRequestListQueryHandler listHandler,
        ICancelDataDeletionRequestCommandHandler cancelHandler)
    {
        _listHandler = listHandler;
        _cancelHandler = cancelHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] DataDeletionStatus? status,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetDataDeletionRequestListQuery(status, cursor, limit), ct);

        return Ok(ApiResponse<IReadOnlyList<DataDeletionRequestDto>>.Ok(
            result.Items, new ApiMeta(result.NextCursor)));
    }

    /// <summary>
    /// Dừng đếm ngược và khôi phục tài khoản. Là đường cứu DUY NHẤT — người gửi yêu cầu bị
    /// soft delete ngay lúc gửi nên không đăng nhập lại được để tự huỷ.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id, [FromBody] CancelDataDeletionRequest request, CancellationToken ct)
    {
        var result = await _cancelHandler.HandleAsync(
            new CancelDataDeletionRequestCommand(CurrentAdminId, id, request.Reason, CurrentIpAddress), ct);

        return Ok(ApiResponse<DataDeletionRequestDto>.Ok(result));
    }
}

public record CancelDataDeletionRequest(string? Reason);
