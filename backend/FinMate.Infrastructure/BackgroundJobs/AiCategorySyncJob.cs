using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Kéo AI Service về cùng danh mục hệ thống với backend: chạy MỘT LẦN lúc khởi động và lặp lại mỗi giờ.
///
/// Đồng bộ ngay sau thao tác của admin chỉ là cố gắng tối đa (AI Service đang tắt không được làm hỏng việc admin vừa làm), nên job này là chỗ bù
/// cho mọi lần đồng bộ hụt: AI Service khởi động sau backend, AI Service được dựng lại với DB trống, hoặc một lời gọi bị rớt. Đồng bộ là cộng thêm
/// và idempotent nên chạy lặp không gây hại.
/// </summary>
public class AiCategorySyncJob
{
    private readonly IAiCategorySyncService _sync;
    private readonly ILogger<AiCategorySyncJob> _logger;

    public AiCategorySyncJob(IAiCategorySyncService sync, ILogger<AiCategorySyncJob> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await _sync.SyncAsync(null, "Đồng bộ danh mục hệ thống từ backend", ct);
        }
        catch (AiRuleException ex)
        {
            // AI Service TỪ CHỐI (xung đột/dữ liệu sai): thử lại y nguyên sẽ bị từ chối y nguyên, nên ghi lỗi để người vận hành sửa chứ không ném cho Hangfire thử lại.
            _logger.LogError(ex, "AI Service rejected the system category sync: {Message}", ex.Message);
        }

        // AIServiceUnavailableException cố ý KHÔNG bắt: ném ra để Hangfire thử lại sau khi AI Service lên.
    }
}
