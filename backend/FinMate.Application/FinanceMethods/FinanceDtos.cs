using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public record ParamDto(string Key, string Label, string Type, long Default, long Min, long Max, bool UserEditable);

public record MethodDto(
    string Code,
    string Name,
    string Summary,
    string Role,
    string GoalMode,
    string WhyText,
    IReadOnlyList<string> StarterSteps,
    IReadOnlyList<ParamDto> Params,
    JsonElement? HomeLayout = null,
    bool Available = true);

public record RecommendedMethodDto(MethodDto Method, string Reason);

/// <param name="Source"><c>ai</c> hoặc <c>rule_fallback</c>.</param>
/// <param name="Outcome"><c>shown</c>, <c>accepted</c>, <c>changed</c> hoặc <c>skipped</c>.</param>
public record RecommendationDto(
    Guid Id,
    RecommendedMethodDto Primary,
    RecommendedMethodDto? Support,
    string Source,
    string Outcome,
    DateTimeOffset CreatedAt);

/// <param name="Overrides">Chỉ phần người dùng đã tự chỉnh.</param>
/// <param name="Effective">Giá trị đang có hiệu lực của MỌI tham số (ghi đè nếu có, không thì mặc định của phương pháp).</param>
public record UserMethodDto(
    Guid Id,
    MethodDto Method,
    string Role,
    string ChosenVia,
    IReadOnlyDictionary<string, long> Overrides,
    IReadOnlyDictionary<string, long> Effective,
    DateTimeOffset StartedAt);

public record SurveyOptionDto(string Code, string Text);

/// <param name="ShowIf">Điều kiện hiển thị (xem <c>ShowIfEvaluator</c>); null = luôn hỏi. Trả cho client để nó ẩn/hiện câu ngay trên máy.</param>
public record SurveyQuestionDto(string Code, string Group, string Text, JsonElement? ShowIf, IReadOnlyList<SurveyOptionDto> Options);

public record SurveyAnswerDto(string QuestionCode, string OptionCode);

/// <param name="Status"><c>pending</c>, <c>completed</c> hoặc <c>skipped</c>.</param>
public record SurveyDto(
    string Status,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<SurveyQuestionDto> Questions,
    IReadOnlyList<SurveyAnswerDto> Answers);

public record GoalSuggestionDto(string TemplateCode, string Name, string Description, JsonElement? DefaultParams, string MethodCode);

public record SpendableGoalDto(Guid GoalId, string Name, long RequiredPerMonthCents, long DepositedThisMonthCents, long SetAsideCents);

/// <summary>
/// "Tiền khả dụng" của tháng hiện tại, tính MỘT chỗ để mọi màn hình dùng chung một con số.
/// </summary>
/// <param name="GoalMode">Cách phương pháp CHÍNH đang dùng đưa khoản để dành vào dòng tiền; null khi chưa dùng phương pháp nào.</param>
/// <param name="IncomeCents">Thu nhập dự kiến người dùng đã khai; null = chưa khai (khi đó <paramref name="SpendableCents"/> cũng null — "chưa biết" không phải "bằng 0").</param>
/// <param name="GoalSetAsideCents">Tổng khoản đã/đang phải dành cho mục tiêu trong tháng theo <paramref name="GoalMode"/>.</param>
public record SpendableDto(
    string? GoalMode,
    long? IncomeCents,
    long SpentCents,
    long GoalSetAsideCents,
    long? SpendableCents,
    IReadOnlyList<SpendableGoalDto> Goals);

internal static class FinanceMapper
{
    public static string GoalModeText(GoalMode mode) => mode switch
    {
        GoalMode.ReserveFirst => "reserve_first",
        GoalMode.Envelope => "envelope",
        GoalMode.AvailableToSpend => "available_to_spend",
        _ => "track_only",
    };

    public static string RoleText(FinanceMethodRole role) => role.ToString().ToLowerInvariant();

    public static MethodDto ToDto(FinanceMethod method) => new(
        method.Code,
        method.Name,
        method.Summary,
        RoleText(method.Role),
        GoalModeText(method.GoalMode),
        method.WhyText,
        FinanceJson.ReadStringList(method.StarterSteps),
        MethodParams.ParseDefs(method.ParamDefs)
            .Select(d => new ParamDto(d.Key, d.Label, d.Type, d.Default, d.Min, d.Max, d.UserEditable))
            .ToList(),
        // Admin luôn lưu ít nhất một khối (kiểm ở FinanceCatalogValidation.HomeLayout); "[]" chỉ là giá trị mặc định của cột chưa ai cấu hình.
        FinanceJson.ToElement(method.HomeLayout is "[]" or "" ? FinanceHomeBlocks.DefaultLayout : method.HomeLayout),
        // false = admin đã tắt cách này: người đang dùng vẫn dùng như cũ nhưng app báo và mời chọn cách khác (danh mục cho người mới chỉ gồm cách đang bật).
        method.IsActive);

    public static UserMethodDto ToDto(UserMethod userMethod, FinanceMethod method)
    {
        var overrides = MethodParams.ParseOverrides(userMethod.Params);
        var effective = MethodParams.Effective(MethodParams.ParseDefs(method.ParamDefs), overrides);
        return new UserMethodDto(
            userMethod.Id,
            ToDto(method),
            userMethod.Role.ToString().ToLowerInvariant(),
            userMethod.ChosenVia.ToString().ToLowerInvariant(),
            overrides,
            effective,
            userMethod.StartedAt);
    }
}
