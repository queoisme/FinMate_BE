namespace FinMate.Application.Agent;

/// <summary>Nguồn của một dữ kiện — quyết định trần độ tin cậy và cách câu trả lời diễn đạt ("đã xác nhận", "bạn nhập", "ước tính", "theo tài liệu").</summary>
public enum EvidenceSource
{
    Confirmed,
    UserEntered,
    Estimated,
    Document,
}

/// <summary>
/// Gói bằng chứng: đơn vị duy nhất mà LLM được phép trích dẫn. Số tiền luôn là <c>long</c> đồng VND (không bao giờ <c>decimal</c>/<c>double</c>), có thể null khi bằng chứng chỉ là chữ.
/// <see cref="Confidence"/> là độ đầy đủ × độ mới (xem <see cref="ConfidenceCalculator"/>), KHÔNG phải xác suất thống kê.
/// </summary>
public sealed record Evidence(
    string Id,
    string Key,
    long? ValueVnd,
    string? Text,
    string Unit,
    EvidenceSource Source,
    DateTimeOffset AsOf,
    double Confidence,
    string? SourceName = null,
    string? SourceUrl = null);

/// <summary>Id và key của bằng chứng tối đa 64 ký tự, cùng giới hạn với <c>ai-service/app/agent/schemas.py</c>; quá thì ai-service trả 422 và câu trả lời rơi về mẫu cục bộ.</summary>
public static class EvidenceKey
{
    public const int MaxLength = 64;

    /// <summary><paramref name="prefix"/> + slug, cắt phần slug cho vừa <see cref="MaxLength"/>.</summary>
    public static string Bounded(string prefix, string slug) => prefix.Length + slug.Length > MaxLength ? (prefix + slug)[..MaxLength] : prefix + slug;
}
