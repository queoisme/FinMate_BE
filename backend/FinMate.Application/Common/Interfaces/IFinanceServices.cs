using FinMate.Application.FinanceMethods;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Khảo sát đầu vào: xem câu hỏi, gửi/bỏ qua/xóa câu trả lời.</summary>
public interface ISurveyService
{
    Task<SurveyDto> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Thay toàn bộ câu trả lời và đánh dấu đã làm xong. Câu trả lời cho câu đang bị ẩn (theo <c>show_if</c>) bị bỏ chứ không lưu.
    /// Đáp án không khớp câu hỏi đang bật, trùng câu hoặc danh sách rỗng → 422.
    /// </summary>
    Task<SurveyDto> SubmitAsync(Guid userId, IReadOnlyList<SurveyAnswerDto> answers, CancellationToken ct = default);

    /// <summary>Bỏ qua khảo sát: chỉ được khi tài khoản còn <c>pending</c>.</summary>
    Task<SurveyDto> SkipAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Xóa câu trả lời VÀ các gợi ý suy ra từ chúng (quyền xóa dữ liệu của chính mình); khảo sát về lại <c>pending</c>.</summary>
    Task<SurveyDto> DeleteAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Gợi ý cách quản lý: AI nếu người dùng đồng ý và AI dùng được, không thì rule.</summary>
public interface IMethodRecommendationService
{
    /// <summary>Tạo gợi ý mới (hoặc trả lại gợi ý cùng signals vừa tạo trong 10 phút gần đây, để bấm liên tục không gọi AI liên tục).</summary>
    Task<RecommendationDto> GenerateAsync(Guid userId, CancellationToken ct = default);

    Task<RecommendationDto> GetLatestAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Người dùng bỏ qua gợi ý: chỉ khi nó còn <c>shown</c>.</summary>
    Task<RecommendationDto> SkipAsync(Guid userId, Guid recommendationId, CancellationToken ct = default);
}

/// <summary>Cách quản lý người dùng đang dùng, và tham số họ tự chỉnh (gồm T1/T2).</summary>
public interface IUserMethodService
{
    Task<IReadOnlyList<MethodDto>> GetCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserMethodDto>> GetMineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Chọn một phương pháp làm cách chính hoặc bổ trợ, thay cách đang giữ vai trò đó. <paramref name="recommendationId"/> ghi lại việc người dùng nhận
    /// hay đổi so với gợi ý (cập nhật <c>outcome</c> của gợi ý đó).
    /// </summary>
    Task<UserMethodDto> ChooseAsync(Guid userId, string methodCode, UserMethodRole role, Guid? recommendationId, CancellationToken ct = default);

    /// <summary>Gộp thay đổi vào phần ghi đè: giá trị null xóa ghi đè (về mặc định của phương pháp). Chỉ tham số cho phép sửa và trong [min, max].</summary>
    Task<UserMethodDto> UpdateParamsAsync(Guid userId, Guid userMethodId, IReadOnlyDictionary<string, long?> changes, CancellationToken ct = default);

    Task EndAsync(Guid userId, Guid userMethodId, CancellationToken ct = default);
}

/// <summary>Mục tiêu theo phương pháp: gợi ý mẫu, tiền khả dụng dùng chung, xem trước tính khả thi.</summary>
public interface IGoalPlanningService
{
    Task<IReadOnlyList<GoalSuggestionDto>> GetSuggestionsAsync(Guid userId, CancellationToken ct = default);

    Task<SpendableDto> GetSpendableAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra khả thi cho một mục tiêu CHƯA tạo — cùng công thức với tiến độ mục tiêu đã có. null khi không có gì để kiểm (đã đủ tiền).
    /// Chỉ là cảnh báo kèm gợi ý, không từ chối.
    /// </summary>
    Task<GoalFeasibilityDto?> PreviewFeasibilityAsync(
        Guid userId, long targetCents, long savedCents, DateTimeOffset deadline, CancellationToken ct = default);
}
