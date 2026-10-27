using System.Security.Claims;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.FinanceMethods;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Danh sách cách quản lý tài chính đang cung cấp và gợi ý cá nhân hóa (AI nếu người dùng đồng ý, không thì rule).</summary>
[ApiController]
[Route("api/v1/finance-methods")]
[Authorize]
public class FinanceMethodsController : ControllerBase
{
    private readonly IUserMethodService _userMethods;
    private readonly IMethodRecommendationService _recommendations;

    public FinanceMethodsController(IUserMethodService userMethods, IMethodRecommendationService recommendations)
    {
        _userMethods = userMethods;
        _recommendations = recommendations;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Mọi cách đang bật, để người dùng tự chọn khi không nhận gợi ý.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<MethodDto>>.Ok(await _userMethods.GetCatalogAsync(ct)));

    /// <summary>Tạo gợi ý từ câu trả lời khảo sát. Cần đã làm khảo sát (422 nếu chưa).</summary>
    [HttpPost("recommendations")]
    public async Task<IActionResult> Recommend(CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<RecommendationDto>.Ok(await _recommendations.GenerateAsync(CurrentUserId, ct)));

    [HttpGet("recommendations/latest")]
    public async Task<IActionResult> GetLatest(CancellationToken ct)
        => Ok(ApiResponse<RecommendationDto>.Ok(await _recommendations.GetLatestAsync(CurrentUserId, ct)));

    [HttpPost("recommendations/{id:guid}/skip")]
    public async Task<IActionResult> Skip(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecommendationDto>.Ok(await _recommendations.SkipAsync(CurrentUserId, id, ct)));
}
