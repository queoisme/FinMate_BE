using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/users")]
public class AdminUsersController : AdminControllerBase
{
    private readonly IGetAdminUserListQueryHandler _listHandler;
    private readonly IGetAdminUserDetailQueryHandler _detailHandler;
    private readonly ISetUserLockCommandHandler _lockHandler;

    public AdminUsersController(
        IGetAdminUserListQueryHandler listHandler,
        IGetAdminUserDetailQueryHandler detailHandler,
        ISetUserLockCommandHandler lockHandler)
    {
        _listHandler = listHandler;
        _detailHandler = detailHandler;
        _lockHandler = lockHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] string? search,
        [FromQuery] UserRole? role,
        [FromQuery] bool? isLocked,
        [FromQuery] bool includeDeleted,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var result = await _listHandler.HandleAsync(
            new GetAdminUserListQuery(search, role, isLocked, includeDeleted, cursor, limit), ct);

        return Ok(ApiResponse<IReadOnlyList<AdminUserDto>>.Ok(
            result.Items, new ApiMeta(result.NextCursor)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct)
    {
        var user = await _detailHandler.HandleAsync(new GetAdminUserDetailQuery(id), ct);
        return Ok(ApiResponse<AdminUserDto>.Ok(user));
    }

    /// <summary>
    /// Nhận TRẠNG THÁI mong muốn thay vì cặp lock/unlock, đúng ví dụ ở CONVENTIONS.md §1.1 —
    /// gọi lại nhiều lần cho cùng kết quả.
    /// </summary>
    [HttpPatch("{id:guid}/lock")]
    public async Task<IActionResult> SetLock(
        Guid id, [FromBody] SetUserLockRequest request, CancellationToken ct)
    {
        var user = await _lockHandler.HandleAsync(
            new SetUserLockCommand(CurrentAdminId, id, request.IsLocked, request.Reason, CurrentIpAddress), ct);

        return Ok(ApiResponse<AdminUserDto>.Ok(user));
    }
}

public record SetUserLockRequest(bool IsLocked, string? Reason);
