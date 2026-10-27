using FinMate.Application.Admin.FinanceCatalog;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>Quản trị câu hỏi khảo sát đầu vào và đáp án (kèm ánh xạ đáp án → signals). Không xóa — tắt bằng activation.</summary>
[Route("api/v1/admin/survey-questions")]
public class AdminSurveyQuestionsController : AdminControllerBase
{
    private readonly IAdminFinanceCatalogService _catalog;

    public AdminSurveyQuestionsController(IAdminFinanceCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<AdminSurveyQuestionDto>>.Ok(await _catalog.ListQuestionsAsync(includeInactive, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSurveyQuestionRequest request, CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<AdminSurveyQuestionDto>.Ok(await _catalog.CreateQuestionAsync(CurrentAdminId, CurrentIpAddress, request, ct)));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSurveyQuestionRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminSurveyQuestionDto>.Ok(await _catalog.UpdateQuestionAsync(CurrentAdminId, CurrentIpAddress, id, request, ct)));

    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminSurveyQuestionDto>.Ok(
            await _catalog.SetQuestionActiveAsync(CurrentAdminId, CurrentIpAddress, id, request.IsActive, ct)));

    [HttpPost("{id:guid}/options")]
    public async Task<IActionResult> AddOption(Guid id, [FromBody] CreateSurveyOptionRequest request, CancellationToken ct)
        => StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<AdminSurveyQuestionDto>.Ok(await _catalog.AddOptionAsync(CurrentAdminId, CurrentIpAddress, id, request, ct)));

    [HttpPatch("{id:guid}/options/{optionId:guid}")]
    public async Task<IActionResult> UpdateOption(Guid id, Guid optionId, [FromBody] UpdateSurveyOptionRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminSurveyQuestionDto>.Ok(
            await _catalog.UpdateOptionAsync(CurrentAdminId, CurrentIpAddress, id, optionId, request, ct)));

    [HttpPatch("{id:guid}/options/{optionId:guid}/activation")]
    public async Task<IActionResult> SetOptionActivation(Guid id, Guid optionId, [FromBody] SetActivationRequest request, CancellationToken ct)
        => Ok(ApiResponse<AdminSurveyQuestionDto>.Ok(
            await _catalog.SetOptionActiveAsync(CurrentAdminId, CurrentIpAddress, id, optionId, request.IsActive, ct)));
}
