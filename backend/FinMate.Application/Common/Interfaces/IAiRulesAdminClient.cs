using System.Text.Json;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Một lời gọi tới API quản trị luật của AI Service. Đi qua nguyên văn: backend không đọc, không lưu dữ liệu luật (luật nằm ở DB của AI Service),
/// chỉ xác thực admin, ghi nhật ký ai đã đổi gì, và gắn người thực hiện dạng băm.
/// </summary>
/// <param name="Path">Đường dẫn dưới <c>/api/v1/admin/{Area}/</c>, đã escape từng đoạn; không có dấu gạch chéo đầu.</param>
/// <param name="ActorId">Admin thực hiện (chỉ với lời gọi ghi). Client băm SHA-256 trước khi gửi, AI Service không bao giờ thấy id thật.</param>
/// <param name="Area">Nhóm API quản trị của AI Service: <c>rules</c> (luật phân loại, mặc định), <c>config</c> (tài liệu cấu hình có phiên bản) hoặc <c>categories</c> (đồng bộ danh mục). Cùng khóa nội bộ, cùng cách xác thực và ánh xạ lỗi.</param>
public sealed record AiRulesRequest(
    HttpMethod Method,
    string Path,
    IReadOnlyDictionary<string, string?>? Query = null,
    JsonElement? Body = null,
    Guid? ActorId = null,
    string Area = AiRulesRequest.RulesArea)
{
    public const string RulesArea = "rules";
    public const string ConfigArea = "config";

    /// <summary>Đồng bộ danh mục hệ thống sang AI Service (<c>PUT categories/sync</c>), xem <c>AiCategorySyncService</c>.</summary>
    public const string CategoriesArea = "categories";

    /// <summary>Định dạng thông báo theo nhà cung cấp (regex, trường, mẫu kiểm tra): <c>/admin/provider-formats</c>.</summary>
    public const string FormatsArea = "provider-formats";

    /// <summary>Trạng thái và thử câu của Rasa (chỉ đọc): <c>/admin/rasa</c>.</summary>
    public const string RasaArea = "rasa";

    /// <summary>Key của bên thứ ba (Gemini, OpenRouter) lưu mã hóa ở AI Service: <c>/admin/secrets</c>. Thân yêu cầu CÓ giá trị key nên không được log ở bất kỳ đâu.</summary>
    public const string SecretsArea = "secrets";
}

public interface IAiRulesAdminClient
{
    /// <returns>Thân JSON của phản hồi; <c>null</c> khi AI Service trả 204.</returns>
    /// <exception cref="Exceptions.AiRuleException">AI Service từ chối: không có (404), trùng (409) hoặc dữ liệu sai (422).</exception>
    /// <exception cref="Exceptions.AIServiceUnavailableException">AI Service không phản hồi hoặc lỗi nội bộ.</exception>
    Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default);
}
