using System.Text.Json;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <summary>
/// Danh sách "Học AI" cho trang admin: câu chat chưa hiểu và phản hồi sửa kết quả AI. Chỉ đọc, chuyển tiếp NGUYÊN VĂN tới <c>/api/v1/admin/learning</c> của AI Service
/// (dữ liệu nằm ở DB của AI Service). Cắt trang và tìm kiếm do AI Service làm: tham số <c>q</c>, <c>limit</c>, <c>offset</c> và bộ lọc đi nguyên văn qua <paramref name="query"/>.
/// AI Service không trả băm người dùng hay mã giao dịch, nên backend cũng không có gì để che. Không ghi nhật ký vì không thay đổi dữ liệu.
/// </summary>
public interface IAdminLearningService
{
    /// <summary>Phản hồi sửa kết quả AI của người dùng (mới nhất trước): <c>{items, total, limit, offset}</c>.</summary>
    Task<JsonElement?> FeedbackAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);
}

public class AdminLearningService : IAdminLearningService
{
    private readonly IAiRulesAdminClient _client;

    public AdminLearningService(IAiRulesAdminClient client)
    {
        _client = client;
    }

    public Task<JsonElement?> FeedbackAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, "feedback", query, Area: AiRulesRequest.LearningArea), ct);
}
