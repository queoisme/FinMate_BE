using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Models;
using FinMate.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Cộng đồng. Ẩn danh là mặc định; không response nào ở đây chứa id tác giả. Chi tiết ở <c>docs/community-api.md</c>.</summary>
[ApiController]
[Route("api/v1/community")]
[Authorize]
[RequireCommunityEnabled]
public class CommunityController : ControllerBase
{
    private readonly ICommunityService _community;
    private readonly ICommunityReadService _read;

    public CommunityController(ICommunityService community, ICommunityReadService read)
    {
        _community = community;
        _read = read;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("topics")]
    public async Task<IActionResult> Topics(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CommunityTopic>>.Ok(await _read.GetTopicsAsync(ct)));

    [HttpGet("regions")]
    public async Task<IActionResult> Regions(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CommunityProvince>>.Ok(await _read.GetRegionsAsync(ct)));

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
        => Ok(ApiResponse<CommunityProfileDto>.Ok(await _community.GetProfileAsync(CurrentUserId, ct)));

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCommunityProfileInput request, CancellationToken ct)
        => Ok(ApiResponse<CommunityProfileDto>.Ok(await _community.UpdateProfileAsync(CurrentUserId, request, ct)));

    [HttpGet("places")]
    public async Task<IActionResult> ListPlaces(
        [FromQuery] string? provinceCode,
        [FromQuery] string? areaCode,
        [FromQuery] string? tag,
        [FromQuery] long? maxPriceCents,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 0,
        CancellationToken ct = default)
        => Ok(ApiResponse<CommunityPlacePageDto>.Ok(await _read.ListPlacesAsync(
            CurrentUserId, new CommunityPlaceQuery(provinceCode, areaCode, tag, maxPriceCents, q, cursor, limit), ct)));

    [HttpGet("places/{id:guid}")]
    public async Task<IActionResult> GetPlace(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CommunityPlaceDetailDto>.Ok(await _read.GetPlaceAsync(CurrentUserId, id, ct)));

    [HttpGet("posts")]
    public async Task<IActionResult> ListPosts(
        [FromQuery] string? topic,
        [FromQuery] string? provinceCode,
        [FromQuery] string? areaCode,
        [FromQuery] string? tag,
        [FromQuery] string? sort,
        [FromQuery] bool mine = false,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 0,
        CancellationToken ct = default)
        => Ok(ApiResponse<CommunityPostPageDto>.Ok(await _read.ListPostsAsync(
            CurrentUserId, new CommunityPostQuery(topic, provinceCode, areaCode, tag, sort, mine, cursor, limit), ct)));

    [HttpGet("posts/{id:guid}")]
    public async Task<IActionResult> GetPost(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CommunityPostDto>.Ok(await _read.GetAsync(CurrentUserId, id, ct)));

    /// <summary>201 khi tạo mới. Chia sẻ lại một quán đã có bài của mình thì cập nhật bài đó và trả 200.</summary>
    [HttpPost("posts")]
    public async Task<IActionResult> CreatePost([FromBody] CreateCommunityPostInput request, CancellationToken ct)
    {
        var result = await _community.CreatePostAsync(CurrentUserId, request, ct);
        var body = ApiResponse<CommunityPostDto>.Ok(result.Post);
        return result.Created ? CreatedAtAction(nameof(GetPost), new { id = result.Post.Id }, body) : Ok(body);
    }

    [HttpPatch("posts/{id:guid}")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdateCommunityPostInput request, CancellationToken ct)
        => Ok(ApiResponse<CommunityPostDto>.Ok(await _community.UpdatePostAsync(CurrentUserId, id, request, ct)));

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken ct)
    {
        await _community.DeletePostAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    [HttpPut("posts/{id:guid}/vote")]
    public async Task<IActionResult> Vote(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CommunityVoteDto>.Ok(await _community.VoteAsync(CurrentUserId, id, ct)));

    [HttpDelete("posts/{id:guid}/vote")]
    public async Task<IActionResult> Unvote(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CommunityVoteDto>.Ok(await _community.UnvoteAsync(CurrentUserId, id, ct)));
}
