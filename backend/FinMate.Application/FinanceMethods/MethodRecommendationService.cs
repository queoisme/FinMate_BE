using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public class MethodRecommendationService : IMethodRecommendationService
{
    // Giới hạn của hợp đồng với ai-service (docs/ai-finance-method-recommend-api.md).
    private const int AiNameMax = 100;
    private const int AiSummaryMax = 400;
    private const int PromptVersionMax = 50;

    private readonly ISurveyRepository _surveys;
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserMethodRepository _userMethods;
    private readonly IMethodAiClient _ai;
    private readonly ISystemConfigProvider _config;

    public MethodRecommendationService(
        ISurveyRepository surveys,
        IFinanceMethodRepository methods,
        IUserMethodRepository userMethods,
        IMethodAiClient ai,
        ISystemConfigProvider config)
    {
        _surveys = surveys;
        _methods = methods;
        _userMethods = userMethods;
        _ai = ai;
        _config = config;
    }

    public async Task<RecommendationDto> GenerateAsync(Guid userId, CancellationToken ct = default)
    {
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

        var settings = (await _config.GetAsync(ct)).Document.FinanceMethods;
        var now = DateTimeOffset.UtcNow;
        var latest = await _userMethods.GetLatestRecommendationAsync(userId, ct);
        // Trong khoảng này, xin gợi ý lại với CÙNG signals thì trả bản vừa tạo: bấm liên tục không gọi AI liên tục.
        if (latest is not null && now - latest.CreatedAt < TimeSpan.FromMinutes(settings.RecommendationReuseMinutes) && SameSignals(FinanceJson.ReadIntMap(latest.SignalsSnapshot), signals))
        {
            return await ToDtoAsync(latest, ct);
        }

        var shortlist = MethodSelector.Shortlist(await _methods.GetMethodsAsync(includeInactive: false, ct), signals);
        if (!shortlist.Any(m => MethodSelector.CanBePrimary(m.Role)))
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.NoMethodAvailable, "Hiện chưa có cách quản lý nào phù hợp để gợi ý cho bạn.");
        }

        var (selection, source, promptVersion) = await SelectAsync(signals, shortlist, settings.SupportMinSharePercent, ct);

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
    /// Luôn nhờ AI chọn (không có bước đồng ý riêng: khảo sát ở đầu hành trình, gợi ý chỉ là gợi ý và người dùng không bị buộc áp dụng; chỉ gửi signals ẩn danh).
    /// Mọi cách AI không cho ra kết quả dùng được (không cấu hình, lỗi, quá hạn, bị backend kiểm lại thấy sai) đều rơi về rule — đường lui duy nhất.
    /// </summary>
    private async Task<(MethodSelection Selection, RecommendationSource Source, string? PromptVersion)> SelectAsync(
        IReadOnlyDictionary<string, int> signals,
        IReadOnlyList<FinanceMethod> shortlist,
        int supportMinSharePercent,
        CancellationToken ct)
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

        // Có ít nhất một cách làm được vai trò chính (đã kiểm ở GenerateAsync) nên SelectByRule không trả null.
        return (MethodSelector.SelectByRule(shortlist, signals, supportMinSharePercent)!, RecommendationSource.RuleFallback, null);
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
