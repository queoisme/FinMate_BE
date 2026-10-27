using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface ISurveyRepository
{
    // ---- câu hỏi và đáp án (dùng chung, admin quản lý) ----

    Task<List<SurveyQuestion>> GetQuestionsAsync(bool includeInactive, CancellationToken ct = default);
    Task<List<SurveyOption>> GetOptionsAsync(bool includeInactive, CancellationToken ct = default);
    Task<SurveyQuestion?> GetQuestionByIdAsync(Guid id, CancellationToken ct = default);
    Task<SurveyQuestion?> GetQuestionByCodeAsync(string code, CancellationToken ct = default);
    Task<SurveyOption?> GetOptionByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tạo câu hỏi cùng các đáp án của nó trong một lần lưu.</summary>
    Task AddQuestionAsync(SurveyQuestion question, IReadOnlyList<SurveyOption> options, CancellationToken ct = default);
    Task UpdateQuestionAsync(SurveyQuestion question, CancellationToken ct = default);
    Task AddOptionAsync(SurveyOption option, CancellationToken ct = default);
    Task UpdateOptionAsync(SurveyOption option, CancellationToken ct = default);

    // ---- câu trả lời của người dùng (luôn lọc theo userId) ----

    Task<List<UserSurveyAnswer>> GetAnswersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Thay toàn bộ câu trả lời của người dùng bằng bộ mới, trong một giao dịch.</summary>
    Task ReplaceAnswersAsync(Guid userId, IReadOnlyList<UserSurveyAnswer> answers, CancellationToken ct = default);

    Task DeleteAnswersAsync(Guid userId, CancellationToken ct = default);
}
