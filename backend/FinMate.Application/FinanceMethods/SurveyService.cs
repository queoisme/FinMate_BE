using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public class SurveyService : ISurveyService
{
    private readonly ISurveyRepository _surveys;
    private readonly IUserMethodRepository _userMethods;
    private readonly IUserRepository _users;
    private readonly ICacheService _cache;

    public SurveyService(
        ISurveyRepository surveys,
        IUserMethodRepository userMethods,
        IUserRepository users,
        ICacheService cache)
    {
        _surveys = surveys;
        _userMethods = userMethods;
        _users = users;
        _cache = cache;
    }

    public async Task<SurveyDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);
        return await BuildAsync(user, ct);
    }

    public async Task<SurveyDto> SubmitAsync(Guid userId, IReadOnlyList<SurveyAnswerDto> answers, CancellationToken ct = default)
    {
        if (answers.Count == 0)
        {
            throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, "Chưa có câu trả lời nào. Muốn bỏ qua khảo sát, hãy dùng chức năng bỏ qua.");
        }

        if (answers.GroupBy(a => a.QuestionCode).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, "Mỗi câu hỏi chỉ được trả lời một lần.");
        }

        var user = await LoadUserAsync(userId, ct);
        var questions = (await _surveys.GetQuestionsAsync(includeInactive: false, ct)).OrderBy(q => q.SortOrder).ToList();
        var options = await _surveys.GetOptionsAsync(includeInactive: false, ct);

        var submitted = answers.ToDictionary(a => a.QuestionCode, a => a.OptionCode);
        foreach (var (questionCode, optionCode) in submitted)
        {
            var question = questions.FirstOrDefault(q => q.Code == questionCode)
                ?? throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, $"Câu hỏi '{questionCode}' không tồn tại hoặc đã tắt.");
            if (!options.Any(o => o.QuestionId == question.Id && o.Code == optionCode))
            {
                throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, $"Đáp án '{optionCode}' không thuộc câu hỏi '{questionCode}'.");
            }
        }

        // Duyệt theo thứ tự câu hỏi: câu chỉ được giữ khi điều kiện hiển thị của nó đúng với các câu TRƯỚC đã giữ. Client gửi bộ đáp án cũ của một câu
        // vừa bị ẩn (vì người dùng đổi câu trước đó) thì bỏ, không để nó lọt vào signals.
        var kept = new Dictionary<string, string>();
        foreach (var question in questions)
        {
            if (submitted.TryGetValue(question.Code, out var optionCode) && ShowIfEvaluator.IsVisible(question.ShowIf, kept))
            {
                kept[question.Code] = optionCode;
            }
        }

        if (kept.Count == 0)
        {
            throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, "Không có câu trả lời nào hợp lệ với các câu hỏi đang hiển thị.");
        }

        var now = DateTimeOffset.UtcNow;
        await _surveys.ReplaceAnswersAsync(
            userId,
            kept.Select(a => new UserSurveyAnswer
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                QuestionCode = a.Key,
                OptionCode = a.Value,
                AnsweredAt = now,
                UpdatedAt = now,
            }).ToList(),
            ct);

        await SetStatusAsync(user, OnboardingStatus.Completed, now, ct);
        return await BuildAsync(user, ct);
    }

    public async Task<SurveyDto> SkipAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);
        if (user.OnboardingStatus != OnboardingStatus.Pending)
        {
            throw new ConflictException(FinanceErrorCodes.OnboardingAlreadyDecided, "Bạn đã làm hoặc đã bỏ qua khảo sát rồi.");
        }

        await SetStatusAsync(user, OnboardingStatus.Skipped, DateTimeOffset.UtcNow, ct);
        return await BuildAsync(user, ct);
    }

    public async Task<SurveyDto> DeleteAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        // Gợi ý lưu signals suy ra từ câu trả lời nên cũng là dữ liệu cần xóa. Phương pháp người dùng ĐANG DÙNG thì giữ: đó là lựa chọn của họ.
        await _userMethods.DeleteRecommendationsAsync(userId, ct);
        await _surveys.DeleteAnswersAsync(userId, ct);

        user.OnboardingStatus = OnboardingStatus.Pending;
        user.OnboardingCompletedAt = null;
        await SaveUserAsync(user, DateTimeOffset.UtcNow, ct);
        return await BuildAsync(user, ct);
    }

    private async Task SetStatusAsync(User user, OnboardingStatus status, DateTimeOffset now, CancellationToken ct)
    {
        user.OnboardingStatus = status;
        user.OnboardingCompletedAt = now;
        await SaveUserAsync(user, now, ct);
    }

    private async Task SaveUserAsync(User user, DateTimeOffset now, CancellationToken ct)
    {
        user.UpdatedAt = now;
        await _users.UpdateAsync(user, ct);
        await _cache.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);
    }

    private async Task<User> LoadUserAsync(Guid userId, CancellationToken ct)
        => await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

    private async Task<SurveyDto> BuildAsync(User user, CancellationToken ct)
    {
        var questions = (await _surveys.GetQuestionsAsync(includeInactive: false, ct)).OrderBy(q => q.SortOrder).ToList();
        var options = (await _surveys.GetOptionsAsync(includeInactive: false, ct))
            .GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.SortOrder).ToList());
        var answers = await _surveys.GetAnswersAsync(user.Id, ct);

        return new SurveyDto(
            user.OnboardingStatus.ToString().ToLowerInvariant(),
            user.OnboardingCompletedAt,
            questions.Select(q => new SurveyQuestionDto(
                q.Code,
                q.GroupCode,
                q.Text,
                FinanceJson.ToElement(q.ShowIf),
                options.GetValueOrDefault(q.Id, []).Select(o => new SurveyOptionDto(o.Code, o.Text)).ToList())).ToList(),
            answers.Select(a => new SurveyAnswerDto(a.QuestionCode, a.OptionCode)).ToList());
    }
}
