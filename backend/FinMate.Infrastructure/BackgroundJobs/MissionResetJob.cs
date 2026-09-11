using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Đóng sổ chu kỳ mission (ARCHITECTURE.md §5, 00:01 giờ VN).
///
/// Job KHÔNG tạo sẵn dòng cho chu kỳ mới: dòng <c>user_missions</c> được sinh lazily khi user
/// thực sự phát sinh hoạt động hoặc mở màn hình mission — cùng cách <c>budget_periods</c> làm
/// ở Phase 5. Tạo sẵn cho mọi user × mọi mission mỗi ngày sẽ đẻ ra hàng loạt dòng tiến độ 0
/// của những user không dùng app.
/// </summary>
public class MissionResetJob
{
    private readonly IMissionRepository _missionRepository;
    private readonly ILogger<MissionResetJob> _logger;

    public MissionResetJob(IMissionRepository missionRepository, ILogger<MissionResetJob> logger)
    {
        _missionRepository = missionRepository;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct = default)
        => RolloverAsync(VietnamTime.Today(), ct);

    public async Task RolloverAsync(DateOnly today, CancellationToken ct = default)
    {
        var expired = await _missionRepository.GetExpiredUserMissionsAsync(today, ct);

        // Dòng hết hạn mà chưa hoàn thành vẫn giữ lại làm lịch sử tiến độ; chu kỳ mới có
        // period_start khác nên không đụng nhau.
        _logger.LogInformation(
            "Mission periods rolled over for {Date}: {ExpiredCount} unfinished missions closed out",
            today,
            expired.Count);
    }
}
