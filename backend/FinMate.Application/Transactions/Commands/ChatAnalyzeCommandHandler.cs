using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

/// <param name="Transcript">Chữ STT nghe được; app cho người dùng xem lại.</param>
public record VoiceAnalyzeDto(string Transcript, ChatParseResponse Parse);

public interface IChatAnalyzeCommandHandler
{
    Task<ChatParseResponse> AnalyzeAsync(Guid userId, string text, CancellationToken ct = default);

    Task<VoiceAnalyzeDto> AnalyzeVoiceAsync(Guid userId, byte[] audio, string fileName, string contentType, CancellationToken ct = default);
}

/// <summary>
/// Chỉ HIỂU câu (hoặc giọng nói rồi hiểu): không đọc, không ghi giao dịch, ví hay báo cáo. App tự quyết định ghi, kiểm trùng và tính
/// tổng bằng dữ liệu trên máy (hướng local-first). Cố ý không nhận repository nào; test cấu trúc giữ điều đó.
/// </summary>
public class ChatAnalyzeCommandHandler(IAIServiceClient ai, ISttServiceClient stt, IValidator<ChatCommand> textRules) : IChatAnalyzeCommandHandler
{
    public async Task<ChatParseResponse> AnalyzeAsync(Guid userId, string text, CancellationToken ct = default)
    {
        await textRules.ValidateAndThrowAsync(new ChatCommand(userId, text), ct);
        return await ai.ParseChatAsync(new ChatParseRequest(userId, text), ct);
    }

    public async Task<VoiceAnalyzeDto> AnalyzeVoiceAsync(Guid userId, byte[] audio, string fileName, string contentType, CancellationToken ct = default)
    {
        var type = VoiceChatCommandHandler.ValidatedType(audio, contentType);
        if (!stt.IsConfigured) throw new AIServiceUnavailableException("STT service chưa được cấu hình (STT_SERVICE_URL).");

        var heard = await stt.TranscribeAsync(new SttRequest(userId, audio, fileName, type), ct);
        var text = heard.Text.Trim();
        if (text.Length == 0)
        {
            throw new BusinessRuleException(TransactionErrorCodes.VoiceNoSpeech, "Không nghe rõ nội dung. Hãy ghi âm lại hoặc gõ tay.");
        }

        return new VoiceAnalyzeDto(text, await AnalyzeAsync(userId, text, ct));
    }
}
