namespace FinMate.Application.Common.Interfaces;

/// <summary>Audio gửi sang stt-service. Audio chỉ đi qua bộ nhớ, không được lưu ở đâu.</summary>
public record SttRequest(Guid UserId, byte[] Audio, string FileName, string ContentType);

/// <param name="Text">Chữ thường, không dấu câu; rỗng khi im lặng hoặc audio không đọc được.</param>
public record SttResponse(string Text, int DurationMs);

public interface ISttServiceClient
{
    /// <summary>False khi STT_SERVICE_URL chưa cấu hình: endpoint voice trả "AI service không khả dụng".</summary>
    bool IsConfigured { get; }

    /// <summary>Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi stt-service không phản hồi hoặc quá tải.</summary>
    Task<SttResponse> TranscribeAsync(SttRequest request, CancellationToken ct = default);
}
