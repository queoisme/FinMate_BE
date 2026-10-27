namespace FinMate.Application.Common.Interfaces;

/// <summary>Một phương pháp trong danh sách được phép (đã qua bộ lọc <c>excludes</c>), đúng các trường hợp đồng với ai-service cho phép gửi.</summary>
public record AiMethodCandidate(string Code, string Name, string Summary, string Role);

/// <summary>Lựa chọn của AI đã qua kiểm tra của ai-service. Backend vẫn kiểm lại (<c>MethodSelector.FromAiPick</c>) trước khi tin.</summary>
public record AiMethodPick(string PrimaryCode, string PrimaryReason, string? SupportCode, string? SupportReason, string PromptVersion);

/// <summary>
/// Gọi <c>POST /api/v1/finance-methods/recommend</c> của ai-service (docs/ai-finance-method-recommend-api.md).
/// Chỉ được gọi khi người dùng có <c>SurveyAiConsentGranted</c> — ai-service không biết người dùng là ai nên không kiểm được điều đó.
/// </summary>
public interface IMethodAiClient
{
    /// <summary>
    /// null khi KHÔNG có kết quả AI dùng được, vì BẤT KỲ lý do gì (chưa cấu hình, quá hạn, 5xx, đầu ra không qua kiểm tra, JSON lạ). Người gọi chạy nhánh rule.
    /// Không bao giờ ném: gợi ý chỉ là tiện ích, không được làm hỏng luồng của người dùng.
    /// </summary>
    Task<AiMethodPick?> RecommendAsync(
        IReadOnlyDictionary<string, int> signals,
        IReadOnlyList<AiMethodCandidate> methods,
        CancellationToken ct = default);
}
