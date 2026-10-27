using System.Text.Json;
using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.FinanceCatalog;

// ---- DTO trả về ----

public record AdminFinanceMethodDto(
    Guid Id,
    string Code,
    string Name,
    string Summary,
    string Role,
    string GoalMode,
    JsonElement? ParamDefs,
    JsonElement? Eligibility,
    string WhyText,
    JsonElement? StarterSteps,
    int SortOrder,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminSurveyOptionDto(Guid Id, string Code, string Text, JsonElement? Signals, int SortOrder, bool IsActive);

public record AdminSurveyQuestionDto(
    Guid Id,
    string Code,
    string GroupCode,
    string Text,
    JsonElement? ShowIf,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<AdminSurveyOptionDto> Options);

public record AdminGoalTemplateDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    JsonElement? DefaultParams,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminMethodTemplateLinkDto(string TemplateCode, JsonElement? Conditions, int SortOrder);

// ---- yêu cầu ----

/// <param name="ParamDefs">Mảng [{key, label, type, default, min, max, userEditable}].</param>
/// <param name="Eligibility">{"weights":{signal:trọng số},"excludes":[signal]}.</param>
/// <param name="StarterSteps">Mảng 1–5 chuỗi.</param>
public record CreateFinanceMethodRequest(
    string Code,
    string Name,
    string Summary,
    FinanceMethodRole Role,
    GoalMode GoalMode,
    JsonElement ParamDefs,
    JsonElement Eligibility,
    string WhyText,
    JsonElement StarterSteps,
    int SortOrder);

/// <summary>Trường nào null thì giữ nguyên. <c>Code</c> chỉ để báo lỗi rõ khi ai đó cố đổi nó — mã bất biến.</summary>
public record UpdateFinanceMethodRequest(
    string? Code,
    string? Name,
    string? Summary,
    FinanceMethodRole? Role,
    GoalMode? GoalMode,
    JsonElement? ParamDefs,
    JsonElement? Eligibility,
    string? WhyText,
    JsonElement? StarterSteps,
    int? SortOrder);

/// <param name="Signals">Object {signal: trọng số nguyên 1–10}.</param>
public record CreateSurveyOptionRequest(string Code, string Text, JsonElement Signals, int SortOrder);

/// <param name="ShowIf">Xem <c>ShowIfEvaluator</c>; null = luôn hỏi.</param>
public record CreateSurveyQuestionRequest(
    string Code,
    string GroupCode,
    string Text,
    JsonElement? ShowIf,
    int SortOrder,
    IReadOnlyList<CreateSurveyOptionRequest> Options);

/// <param name="ClearShowIf">true = gỡ điều kiện hiển thị (câu luôn được hỏi). Cần cờ riêng vì JSON null và "không gửi" giống nhau với <c>ShowIf</c>.</param>
public record UpdateSurveyQuestionRequest(
    string? Code,
    string? GroupCode,
    string? Text,
    JsonElement? ShowIf,
    bool? ClearShowIf,
    int? SortOrder);

public record UpdateSurveyOptionRequest(string? Code, string? Text, JsonElement? Signals, int? SortOrder);

public record CreateGoalTemplateRequest(string Code, string Name, string Description, JsonElement DefaultParams);

public record UpdateGoalTemplateRequest(string? Code, string? Name, string? Description, JsonElement? DefaultParams);

/// <param name="Conditions">{"excludes":[signal]} hoặc null (luôn gợi ý).</param>
public record MethodTemplateLinkRequest(string TemplateCode, JsonElement? Conditions, int SortOrder);

/// <summary>Quản trị danh mục cách quản lý tài chính, khảo sát và mẫu mục tiêu. Mọi thao tác GHI để lại một dòng audit; không xóa cứng, tắt bằng activation.</summary>
public interface IAdminFinanceCatalogService
{
    Task<IReadOnlyList<AdminFinanceMethodDto>> ListMethodsAsync(bool includeInactive, CancellationToken ct = default);
    Task<AdminFinanceMethodDto> CreateMethodAsync(Guid adminId, string? ip, CreateFinanceMethodRequest request, CancellationToken ct = default);
    Task<AdminFinanceMethodDto> UpdateMethodAsync(Guid adminId, string? ip, Guid id, UpdateFinanceMethodRequest request, CancellationToken ct = default);
    Task<AdminFinanceMethodDto> SetMethodActiveAsync(Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default);
    Task<IReadOnlyList<AdminMethodTemplateLinkDto>> GetMethodTemplatesAsync(Guid methodId, CancellationToken ct = default);

    /// <summary>Thay TOÀN BỘ liên kết mẫu mục tiêu của một phương pháp.</summary>
    Task<IReadOnlyList<AdminMethodTemplateLinkDto>> ReplaceMethodTemplatesAsync(
        Guid adminId, string? ip, Guid methodId, IReadOnlyList<MethodTemplateLinkRequest> links, CancellationToken ct = default);

    Task<IReadOnlyList<AdminSurveyQuestionDto>> ListQuestionsAsync(bool includeInactive, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> CreateQuestionAsync(Guid adminId, string? ip, CreateSurveyQuestionRequest request, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> UpdateQuestionAsync(Guid adminId, string? ip, Guid id, UpdateSurveyQuestionRequest request, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> SetQuestionActiveAsync(Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> AddOptionAsync(Guid adminId, string? ip, Guid questionId, CreateSurveyOptionRequest request, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> UpdateOptionAsync(
        Guid adminId, string? ip, Guid questionId, Guid optionId, UpdateSurveyOptionRequest request, CancellationToken ct = default);
    Task<AdminSurveyQuestionDto> SetOptionActiveAsync(
        Guid adminId, string? ip, Guid questionId, Guid optionId, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyList<AdminGoalTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct = default);
    Task<AdminGoalTemplateDto> CreateTemplateAsync(Guid adminId, string? ip, CreateGoalTemplateRequest request, CancellationToken ct = default);
    Task<AdminGoalTemplateDto> UpdateTemplateAsync(Guid adminId, string? ip, Guid id, UpdateGoalTemplateRequest request, CancellationToken ct = default);
    Task<AdminGoalTemplateDto> SetTemplateActiveAsync(Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default);
}
