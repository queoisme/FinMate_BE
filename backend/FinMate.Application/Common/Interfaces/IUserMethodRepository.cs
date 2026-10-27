using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Phương pháp người dùng đang/đã dùng và nhật ký các lần gợi ý. Mọi hàm đều lọc theo <c>userId</c>.</summary>
public interface IUserMethodRepository
{
    Task<List<UserMethod>> GetActiveAsync(Guid userId, CancellationToken ct = default);
    Task<UserMethod?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task UpdateAsync(UserMethod userMethod, CancellationToken ct = default);

    /// <summary>
    /// Kết thúc <paramref name="ended"/> (nếu có) rồi thêm <paramref name="added"/> trong MỘT giao dịch. Làm hai bước vì chỉ mục duy nhất
    /// từng phần (một cách chính và một bổ trợ đang hoạt động) sẽ vi phạm nếu dòng mới được chèn trước khi dòng cũ đóng.
    /// </summary>
    Task ReplaceActiveAsync(UserMethod? ended, UserMethod added, CancellationToken ct = default);

    Task<MethodRecommendation?> GetRecommendationAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<MethodRecommendation?> GetLatestRecommendationAsync(Guid userId, CancellationToken ct = default);
    Task AddRecommendationAsync(MethodRecommendation recommendation, CancellationToken ct = default);
    Task UpdateRecommendationAsync(MethodRecommendation recommendation, CancellationToken ct = default);

    /// <summary>Xóa mọi gợi ý của người dùng (chúng chứa signals suy ra từ câu trả lời). <c>user_methods.recommendation_id</c> tự về NULL.</summary>
    Task DeleteRecommendationsAsync(Guid userId, CancellationToken ct = default);
}
