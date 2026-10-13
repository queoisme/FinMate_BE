using System.Security.Claims;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Phân tích câu chat hoặc giọng nói mà KHÔNG chạm dữ liệu cá nhân: trả nguyên kết quả AI Service, app tự quyết định ghi gì.
/// Đường <c>POST /transactions/chat</c> cũ (backend ghi giao dịch) vẫn còn song song.
/// </summary>
[ApiController]
[Route("api/v1/chat")]
[Authorize]
public class ChatAnalyzeController(IChatAnalyzeCommandHandler handler) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request, CancellationToken ct)
        => Ok(ApiResponse<ChatParseResponse>.Ok(await handler.AnalyzeAsync(CurrentUserId, request.Text, ct)));

    [HttpPost("analyze-voice")]
    [RequestSizeLimit(VoiceChatCommandHandler.MaxAudioBytes + 4096)]
    public async Task<IActionResult> AnalyzeVoice(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new BusinessRuleException(TransactionErrorCodes.VoiceAudioInvalid, "Vui lòng ghi âm hoặc chọn tệp âm thanh.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return Ok(ApiResponse<VoiceAnalyzeDto>.Ok(
            await handler.AnalyzeVoiceAsync(CurrentUserId, buffer.ToArray(), file.FileName, file.ContentType ?? string.Empty, ct)));
    }
}

public record AnalyzeRequest(string Text);
