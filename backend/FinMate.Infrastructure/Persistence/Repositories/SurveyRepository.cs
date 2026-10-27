using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SurveyRepository : ISurveyRepository
{
    private readonly FinMateDbContext _context;

    public SurveyRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<SurveyQuestion>> GetQuestionsAsync(bool includeInactive, CancellationToken ct = default)
        => _context.SurveyQuestions
            .AsNoTracking()
            .Where(q => includeInactive || q.IsActive)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(ct);

    public Task<List<SurveyOption>> GetOptionsAsync(bool includeInactive, CancellationToken ct = default)
        => _context.SurveyOptions
            .AsNoTracking()
            .Where(o => includeInactive || o.IsActive)
            .OrderBy(o => o.SortOrder)
            .ToListAsync(ct);

    public Task<SurveyQuestion?> GetQuestionByIdAsync(Guid id, CancellationToken ct = default)
        => _context.SurveyQuestions.FirstOrDefaultAsync(q => q.Id == id, ct);

    public Task<SurveyQuestion?> GetQuestionByCodeAsync(string code, CancellationToken ct = default)
        => _context.SurveyQuestions.FirstOrDefaultAsync(q => q.Code == code, ct);

    public Task<SurveyOption?> GetOptionByIdAsync(Guid id, CancellationToken ct = default)
        => _context.SurveyOptions.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddQuestionAsync(SurveyQuestion question, IReadOnlyList<SurveyOption> options, CancellationToken ct = default)
    {
        _context.SurveyQuestions.Add(question);
        _context.SurveyOptions.AddRange(options);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateQuestionAsync(SurveyQuestion question, CancellationToken ct = default)
    {
        _context.SurveyQuestions.Update(question);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddOptionAsync(SurveyOption option, CancellationToken ct = default)
    {
        _context.SurveyOptions.Add(option);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateOptionAsync(SurveyOption option, CancellationToken ct = default)
    {
        _context.SurveyOptions.Update(option);
        await _context.SaveChangesAsync(ct);
    }

    public Task<List<UserSurveyAnswer>> GetAnswersAsync(Guid userId, CancellationToken ct = default)
        => _context.UserSurveyAnswers.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(ct);

    public async Task ReplaceAnswersAsync(Guid userId, IReadOnlyList<UserSurveyAnswer> answers, CancellationToken ct = default)
    {
        // Gộp theo mã câu hỏi (UNIQUE user_id + question_code): câu đã có thì đổi đáp án, câu không còn thì xóa, câu mới thì thêm — trong MỘT lần lưu.
        var existing = await _context.UserSurveyAnswers.Where(a => a.UserId == userId).ToListAsync(ct);
        var wanted = answers.ToDictionary(a => a.QuestionCode);

        foreach (var answer in existing)
        {
            if (wanted.Remove(answer.QuestionCode, out var replacement))
            {
                answer.OptionCode = replacement.OptionCode;
                answer.AnsweredAt = replacement.AnsweredAt;
                answer.UpdatedAt = replacement.UpdatedAt;
            }
            else
            {
                _context.UserSurveyAnswers.Remove(answer);
            }
        }

        _context.UserSurveyAnswers.AddRange(wanted.Values);
        await _context.SaveChangesAsync(ct);
    }

    public Task DeleteAnswersAsync(Guid userId, CancellationToken ct = default)
        => _context.UserSurveyAnswers.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
}
