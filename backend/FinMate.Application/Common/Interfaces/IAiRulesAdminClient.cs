using System.Text.Json;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Một lời gọi tới API quản trị luật của AI Service. Đi qua nguyên văn: backend không đọc, không lưu dữ liệu luật (luật nằm ở DB của AI Service),
/// chỉ xác thực admin, ghi nhật ký ai đã đổi gì, và gắn người thực hiện dạng băm.
/// </summary>
/// <param name="Path">Đường dẫn dưới <c>/api/v1/admin/rules/</c>, đã escape từng đoạn; không có dấu gạch chéo đầu.</param>
/// <param name="ActorId">Admin thực hiện (chỉ với lời gọi ghi). Client băm SHA-256 trước khi gửi, AI Service không bao giờ thấy id thật.</param>
public sealed record AiRulesRequest(
    HttpMethod Method,
    string Path,
    IReadOnlyDictionary<string, string?>? Query = null,
    JsonElement? Body = null,
    Guid? ActorId = null);

public interface IAiRulesAdminClient
{
    /// <returns>Thân JSON của phản hồi; <c>null</c> khi AI Service trả 204.</returns>
    /// <exception cref="Exceptions.AiRuleException">AI Service từ chối: không có (404), trùng (409) hoặc dữ liệu sai (422).</exception>
    /// <exception cref="Exceptions.AIServiceUnavailableException">AI Service không phản hồi hoặc lỗi nội bộ.</exception>
    Task<JsonElement?> SendAsync(AiRulesRequest request, CancellationToken ct = default);
}
