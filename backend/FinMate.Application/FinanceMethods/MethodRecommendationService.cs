using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public class MethodRecommendationService : IMethodRecommendationService
{
    /// <summary>Trong khoảng này, xin gợi ý lại với CÙNG signals thì trả bản vừa tạo — bấm liên tục không gọi AI liên tục.</summary>
    private static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(10);

    // Giới hạn của hợp đồng với ai-service (docs/ai-finance-method-recommend-api.md).
    private const int AiNameMax = 100;
    private const int AiSummaryMax = 400;
    private const int PromptVersionMax = 50;

    private readonly ISurveyRepository _surveys;
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserMethodRepository _userMethods;
    private readonly IUserRepository _users;
    private readonly IMethodAiClient _ai;

    public MethodRecommendationService(
        ISurveyRepository surveys,
        IFinanceMethodRepository methods,
        IUserMethodRepository userMethods,
        IUserRepository users,
        IMethodAiClient ai)
    {
        _surveys = surveys;
        _methods = methods;
        _userMethods = userMethods;
        _users = users;
        _ai = ai;
    }

    public async Task<RecommendationDto> GenerateAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

        var answers = await _surveys.GetAnswersAsync(userId, ct);
        var signals = SurveySignals.FromAnswers(
            answers,
            await _surveys.GetQuestionsAsync(includeInactive: true, ct),
            await _surveys.GetOptionsAsync(includeInactive: true, ct));
        if (signals.Count == 0)
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.SurveyNotAnswered, "Chưa có câu trả lời khảo sát nào để gợi ý. Hãy làm khảo sát trước.");
        }

        var now = DateTimeOffset.UtcNow;
        var latest = await _userMethods.GetLatestRecommendationAsync(userId, ct);
        if (latest is not null && now - latest.CreatedAt < ReuseWindow && SameSignals(FinanceJson.ReadIntMap(latest.SignalsSnapshot), signals))
        {
            return await ToDtoAsync(latest, ct);
        }

        var shortlist = MethodSelector.Shortlist(await _methods.GetMethodsAsync(includeInactive: false, ct), signals);
        if (!shortlist.Any(m => MethodSelector.CanBePrimary(m.Role)))
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.NoMethodAvailable, "Hiện chưa có cách quản lý nào phù hợp để gợi ý cho bạn.");
        }

        var (selection, source, promptVersion) = await SelectAsync(user, signals, shortlist, ct);

        var recommendation = new MethodRecommendation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PrimaryMethodId = selection.Primary.Id,
            SupportMethodId = selection.Support?.Id,
            Reasons = FinanceJson.Serialize(BuildReasons(selection)),
            SignalsSnapshot = FinanceJson.Serialize(signals),
            Source = source,
            PromptVersion = promptVersion,
            MethodVersions = FinanceJson.Serialize(shortlist.ToDictionary(m => m.Code, m => m.Version)),
            Outcome = RecommendationOutcome.Shown,
            CreatedAt = now,
        };
        await _userMethods.AddRecommendationAsync(recommendation, ct);

        return await ToDtoAsync(recommendation, ct);
    }

    public async Task<RecommendationDto> GetLatestAsync(Guid userId, CancellationToken ct = default)
    {
        var latest = await _userMethods.GetLatestRecommendationAsync(userId, ct)
            ?? throw new NotFoundException("Recommendation", "latest");
        return await ToDtoAsync(latest, ct);
    }

    public async Task<RecommendationDto> SkipAsync(Guid userId, Guid recommendationId, CancellationToken ct = default)
    {
        var recommendation = await _userMethods.GetRecommendationAsync(recommendationId, userId, ct)
            ?? throw new NotFoundException("Recommendation", recommendationId);
        if (recommendation.Outcome != RecommendationOutcome.Shown)
        {
            throw new ConflictException(FinanceErrorCodes.RecommendationAlreadyDecided, "Bạn đã quyết định với gợi ý này rồi.");
        }

        recommendation.Outcome = RecommendationOutcome.Skipped;
        recommendation.DecidedAt = DateTimeOffset.UtcNow;
        await _userMethods.UpdateRecommendationAsync(recommendation, ct);
        return await ToDtoAsync(recommendation, ct);
    }

    /// <summary>
    /// Chỉ gọi AI khi người dùng đã đồng ý riêng cho khảo sát; mọi cách AI không cho ra kết quả dùng được (không có, lỗi, bị backend kiểm lại thấy sai)
    /// đều rơi về rule — đường lui duy nhất, và cũng là đường chính khi không có đồng ý.
    /// </summary>
    private async Task<(MethodSelection Selection, RecommendationSource Source, string? PromptVersion)> SelectAsync(
        User user,
        IReadOnlyDictionary<string, int> signals,
        IReadOnlyList<FinanceMethod> shortlist,
        CancellationToken ct)
    {
        if (user.SurveyAiConsentGranted)
        {
            var candidates = shortlist
                .Select(m => new AiMethodCandidate(
                    m.Code, Truncate(m.Name, AiNameMax), Truncate(m.Summary, AiSummaryMax), FinanceMapper.RoleText(m.Role)))
                .ToList();

            var pick = await _ai.RecommendAsync(signals, candidates, ct);
            if (pick is not null
                && MethodSelector.FromAiPick(pick.PrimaryCode, pick.PrimaryReason, pick.SupportCode, pick.SupportReason, shortlist) is { } fromAi)
            {
                return (fromAi, RecommendationSource.Ai, Truncate(pick.PromptVersion, PromptVersionMax));
            }
        }

        // Có ít nhất một cách làm được vai trò chính (đã kiểm ở GenerateAsync) nên SelectByRule không trả null.
        return (MethodSelector.SelectByRule(shortlist, signals)!, RecommendationSource.RuleFallback, null);
    }

    private async Task<RecommendationDto> ToDtoAsync(MethodRecommendation recommendation, CancellationToken ct)
    {
        var ids = new[] { recommendation.PrimaryMethodId, recommendation.SupportMethodId ?? Guid.Empty };
        var methods = (await _methods.GetMethodsByIdsAsync(ids, ct)).ToDictionary(m => m.Id);
        var reasons = ReadReasons(recommendation.Reasons);

        RecommendedMethodDto? Build(Guid? id, string role)
            => id is { } methodId && methods.TryGetValue(methodId, out var method)
                ? new RecommendedMethodDto(FinanceMapper.ToDto(method), reasons.GetValueOrDefault(role) ?? method.WhyText)
                : null;

        return new RecommendationDto(
            recommendation.Id,
            Build(recommendation.PrimaryMethodId, "primary")
                ?? throw new NotFoundException("FinanceMethod", recommendation.PrimaryMethodId),
            Build(recommendation.SupportMethodId, "support"),
            recommendation.Source == RecommendationSource.Ai ? "ai" : "rule_fallback",
            recommendation.Outcome.ToString().ToLowerInvariant(),
            recommendation.CreatedAt);
    }

    private static List<object> BuildReasons(MethodSelection selection)
    {
        var reasons = new List<object> { new { role = "primary", code = selection.Primary.Code, reason = selection.PrimaryReason } };
        if (selection.Support is not null && selection.SupportReason is not null)
        {
            reasons.Add(new { role = "support", code = selection.Support.Code, reason = selection.SupportReason });
        }

        return reasons;
    }

    private static Dictionary<string, string> ReadReasons(string json)
    {
        var reasons = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("role", out var role) && item.TryGetProperty("reason", out var reason)
                    && role.ValueKind == JsonValueKind.String && reason.ValueKind == JsonValueKind.String)
                {
                    reasons[role.GetString()!] = reason.GetString()!;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Lý do hỏng thì dùng WhyText của phương pháp.
        }

        return reasons;
    }

    private static bool SameSignals(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
