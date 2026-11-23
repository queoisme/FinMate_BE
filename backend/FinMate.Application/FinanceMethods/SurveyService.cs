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

    /// <summary>Trần của một câu trả lời số tiền (100 tỷ đồng), cùng trần với số tiền ở các công cụ của agent.</summary>
    public const long MaxAmountVnd = 100_000_000_000;

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

        var submitted = answers.ToDictionary(a => a.QuestionCode);
        foreach (var answer in answers)
        {
            var question = questions.FirstOrDefault(q => q.Code == answer.QuestionCode)
                ?? throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, $"Câu hỏi '{answer.QuestionCode}' không tồn tại hoặc đã tắt.");
            if (question.AnswerType == SurveyAnswerTypes.Amount)
            {
                if (!string.IsNullOrEmpty(answer.OptionCode) || answer.AmountVnd is not (>= 0 and <= MaxAmountVnd))
                {
                    throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, $"Câu hỏi '{answer.QuestionCode}' cần một số tiền từ 0 đến 100 tỷ đồng.");
                }
            }
            else if (answer.AmountVnd is not null || !options.Any(o => o.QuestionId == question.Id && o.Code == answer.OptionCode))
            {
                throw new BusinessRuleException(FinanceErrorCodes.SurveyAnswerInvalid, $"Đáp án '{answer.OptionCode}' không thuộc câu hỏi '{answer.QuestionCode}'.");
            }
        }

        // Duyệt theo thứ tự câu hỏi: câu chỉ được giữ khi điều kiện hiển thị của nó đúng với các câu TRƯỚC đã giữ. Client gửi bộ đáp án cũ của một câu
        // vừa bị ẩn (vì người dùng đổi câu trước đó) thì bỏ, không để nó lọt vào signals. Điều kiện chỉ đọc mã đáp án của câu choice.
        var kept = new Dictionary<string, SurveyAnswerDto>();
        var keptOptions = new Dictionary<string, string>();
        foreach (var question in questions)
        {
            if (submitted.TryGetValue(question.Code, out var answer) && ShowIfEvaluator.IsVisible(question.ShowIf, keptOptions))
            {
                kept[question.Code] = answer;
                if (answer.OptionCode is { Length: > 0 } optionCode)
                {
                    keptOptions[question.Code] = optionCode;
                }
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
                OptionCode = a.Value.OptionCode ?? string.Empty,
                AmountVnd = a.Value.AmountVnd,
                AnsweredAt = now,
                UpdatedAt = now,
            }).ToList(),
            ct);

        // Thu nhập khai trong khảo sát là thu nhập dự kiến mới nhất của người dùng: ghi vào hồ sơ cùng lần lưu trạng thái.
        var incomeQuestion = questions.FirstOrDefault(q => q.PlanRole == SurveyPlanRoles.Income && kept.ContainsKey(q.Code));
        if (incomeQuestion is not null)
        {
            user.MonthlyIncomeCents = kept[incomeQuestion.Code].AmountVnd;
        }

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
                options.GetValueOrDefault(q.Id, []).Select(o => new SurveyOptionDto(o.Code, o.Text, o.ShareMinPct, o.ShareMaxPct)).ToList(),
                q.AnswerType,
                q.CategorySlug,
                q.PlanRole)).ToList(),
            answers.Select(a => new SurveyAnswerDto(a.QuestionCode, a.AmountVnd is null ? a.OptionCode : null, a.AmountVnd)).ToList());
    }
}
