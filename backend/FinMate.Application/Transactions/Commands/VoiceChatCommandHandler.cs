using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

public record VoiceChatCommand(Guid UserId, byte[] Audio, string FileName, string ContentType,
    Guid? FinancialAccountId, Guid? ClientRequestId);

/// <param name="Transcript">Chữ STT nghe được; client hiện lại cho người dùng kiểm tra.</param>
/// <param name="Chat">Kết quả y hệt gõ chữ vào chat (ghi, nháp nhiều khoản, hỏi tổng...).</param>
public record VoiceChatResultDto(string Transcript, ChatResultDto Chat);

public interface IVoiceChatCommandHandler
{
    Task<VoiceChatResultDto> HandleAsync(VoiceChatCommand command, CancellationToken ct = default);
}

/// <summary>
/// Giọng nói → chữ (stt-service) → đúng luồng chat có sẵn. Không có luật hiểu riêng cho voice: danh mục, nhiều khoản, ngày,
/// trùng lặp và xác nhận đều do <see cref="IChatCommandHandler"/> quyết như câu gõ tay. Audio không được lưu.
/// </summary>
public class VoiceChatCommandHandler(ISttServiceClient stt, IChatCommandHandler chat) : IVoiceChatCommandHandler
{
    public const int MaxAudioBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AudioTypes =
        ["audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/aac", "audio/ogg", "audio/webm", "audio/flac"];

    public async Task<VoiceChatResultDto> HandleAsync(VoiceChatCommand command, CancellationToken ct = default)
    {
        var type = ValidatedType(command.Audio, command.ContentType);

        if (!stt.IsConfigured) throw new AIServiceUnavailableException("STT service chưa được cấu hình (STT_SERVICE_URL).");

        var heard = await stt.TranscribeAsync(new SttRequest(command.UserId, command.Audio, command.FileName, type), ct);
        var text = heard.Text.Trim();
        if (text.Length == 0)
        {
            throw new BusinessRuleException(TransactionErrorCodes.VoiceNoSpeech, "Không nghe rõ nội dung. Hãy ghi âm lại hoặc gõ tay.");
        }

        var result = await chat.HandleAsync(new ChatCommand(command.UserId, text, command.FinancialAccountId, command.ClientRequestId), ct);
        return new VoiceChatResultDto(text, result);
    }

    /// <summary>Kiểu âm thanh đã chuẩn hóa; ném <see cref="TransactionErrorCodes.VoiceAudioInvalid"/> khi tệp rỗng, quá lớn hoặc sai định dạng.</summary>
    public static string ValidatedType(byte[] audio, string contentType)
    {
        var type = NormalizeType(contentType);
        if (audio.Length is 0 or > MaxAudioBytes || !AudioTypes.Contains(type))
        {
            throw new BusinessRuleException(TransactionErrorCodes.VoiceAudioInvalid,
                "Âm thanh phải có từ 1 byte đến 10 MiB và đúng định dạng (m4a, wav, mp3, ogg, webm, flac).");
        }

        return type;
    }

    // m4a của iOS/Android khai là audio/m4a hoặc audio/x-m4a, nhưng stt-service chỉ nhận audio/mp4 (cùng container).
    private static string NormalizeType(string type)
    {
        var t = type.Split(';', 2)[0].Trim().ToLowerInvariant();
        return t is "audio/m4a" or "audio/x-m4a" ? "audio/mp4" : t;
    }
}
