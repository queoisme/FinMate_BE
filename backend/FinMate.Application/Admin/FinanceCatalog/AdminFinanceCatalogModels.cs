using System.Text.Json;
using FinMate.Domain.Entities;
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
    JsonElement? HomeLayout,
    int SortOrder,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ActiveUserCount = 0);

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
/// <param name="HomeLayout">Mảng 1–8 khối trang chủ [{type, title?, keys?, display?}]; null = bố cục mặc định. Danh sách loại khối: <c>GET home-blocks</c>.</param>
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
    int SortOrder,
    JsonElement? HomeLayout = null);

/// <summary>Một loại khối giao diện trang chủ mà app biết vẽ, để màn admin liệt kê lựa chọn mà không giữ bản sao danh sách.</summary>
/// <param name="AcceptsKeys">Có nhận <c>keys</c> (lọc theo khóa tham số) hay không.</param>
/// <param name="AcceptsDisplay">Có nhận <c>display</c> (<c>list</c> hoặc <c>allocation</c>) hay không.</param>
/// <param name="Repeatable">Có được xuất hiện nhiều lần trong một bố cục hay không.</param>
/// <param name="AcceptsJars">Có nhận danh sách <c>jars</c> (hũ: tham số % + danh mục) hay không.</param>
public record HomeBlockTypeDto(string Type, string Name, string Description, bool AcceptsKeys, bool AcceptsDisplay, bool Repeatable, bool AcceptsJars = false);

public static class FinanceHomeBlockCatalog
{
    public static readonly IReadOnlyList<HomeBlockTypeDto> All =
    [
        new(FinanceHomeBlocks.Spendable, "Tiền khả dụng", "Thu nhập, đã chi, đã dành cho mục tiêu và số còn được chi trong tháng.", false, false, false),
        new(FinanceHomeBlocks.Params, "Tham số", "Giá trị đang có hiệu lực của các tham số. Chọn tham số cần hiện; dạng chia phần trăm vẽ thanh theo % thu nhập (chỉ dùng cho tham số kiểu %).", true, true, true),
        new(FinanceHomeBlocks.Jars, "Các hũ", "Chia thu nhập thành các hũ: mỗi hũ là một tham số kiểu % của cách này kèm các danh mục thuộc hũ. App so chi tiêu tháng này của các danh mục đó với số tiền được chia và báo theo ngưỡng cảnh báo của người dùng.", false, false, false, true),
        new(FinanceHomeBlocks.Goals, "Mục tiêu", "Mục tiêu đang dành tiền và mẫu mục tiêu gợi ý.", false, false, false),
        new(FinanceHomeBlocks.StarterSteps, "Bước bắt đầu", "Các bước bắt đầu của cách quản lý này.", false, false, false),
    ];
}

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
    int? SortOrder,
    JsonElement? HomeLayout = null);

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
