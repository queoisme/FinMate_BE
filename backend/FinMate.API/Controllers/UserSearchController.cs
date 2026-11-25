using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Tìm người để mời vào nhóm. Khớp chính xác theo email (tài khoản đã xác minh) hoặc FinMate ID; không tìm gần đúng, và có giới hạn số lần
/// cho mỗi người gọi (429 <c>USER_SEARCH_RATE_LIMITED</c>) để không dùng làm công cụ dò danh sách người dùng. Số điện thoại chưa được hỗ trợ.
/// </summary>
[ApiController]
[Route("api/v1/users/search")]
[Authorize]
[RequireGroupsEnabled]
public class UserSearchController : ControllerBase
{
    private readonly IUserSearchService _search;

    public UserSearchController(IUserSearchService search)
    {
        _search = search;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <param name="groupId">Có thì cần quyền mời của nhóm đó; người đã trong nhóm bị loại khỏi kết quả và người đã có lời mời chờ được đánh dấu <c>alreadyInvited</c>.</param>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] Guid? groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<UserSearchResultDto>>.Ok(await _search.SearchAsync(CurrentUserId, q, groupId, ct)));
}
