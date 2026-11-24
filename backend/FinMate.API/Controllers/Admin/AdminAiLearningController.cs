using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// "Học AI" cho màn hình admin trên web: danh sách câu chat AI chưa hiểu và phản hồi sửa kết quả của người dùng. Chỉ đọc; controller chỉ giữ cổng (AdminOnly) và chuyển tiếp
/// tới <c>/api/v1/admin/learning</c> của AI Service. Cắt trang và tìm kiếm do AI Service làm (<c>q</c>, <c>limit</c> 1–200 mặc định 20, <c>offset</c>); mỗi phản hồi là
/// <c>{items, total, limit, offset}</c> với tên trường snake_case của AI Service. Không có băm người dùng hay mã giao dịch trong dữ liệu trả về. Duyệt hoặc loại câu vẫn đi qua <c>scripts/nlu_candidates.py</c>.
/// </summary>
[Route("api/v1/admin/ai-learning")]
public class AdminAiLearningController : AdminControllerBase
{
    private readonly IAdminLearningService _service;

    public AdminAiLearningController(IAdminLearningService service)
    {
        _service = service;
    }

    private IReadOnlyDictionary<string, string?> QueryOf()
        => Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    /// <summary>
    /// Phản hồi sửa kết quả AI (mới nhất trước): <c>id, package_name, predicted_category, corrected_category, feedback_type, processed, created_at</c>.
    /// Lọc: <c>q</c> (gói ứng dụng hoặc danh mục), <c>feedback_type</c> (category_correction | amount_correction | not_a_transaction), <c>processed</c> (true | false).
    /// </summary>
    [HttpGet("feedback")]
    public async Task<IActionResult> Feedback(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.FeedbackAsync(QueryOf(), ct)));

    /// <summary>Tổng quan: số phản hồi theo loại, số chưa dùng để học, các cặp (AI đoán → người dùng sửa) nhiều nhất.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.SummaryAsync(ct)));

    /// <summary>
    /// Gợi ý từ khóa cho từ điển tên cửa hàng: tên mà từ 2 người dùng khác nhau trở lên cùng sửa sang một danh mục
    /// (<c>keyword, corrected_category, users, times, predicted_categories, current_category, covered, last_at</c>). Lọc: <c>min_users</c> (≥ 2), <c>include_covered</c>.
    /// Thêm vào từ điển đi qua tài liệu <c>merchant_lexicon</c> (/admin/ai-config), có lịch sử và khôi phục.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.SuggestionsAsync(QueryOf(), ct)));
}
