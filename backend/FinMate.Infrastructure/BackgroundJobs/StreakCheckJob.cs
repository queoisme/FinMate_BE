using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.BackgroundJobs;

/// <summary>
/// Reset chuỗi ngày cho user không có hoạt động nào trong hôm nay (ARCHITECTURE.md §5,
/// 23:55 giờ VN). Chạy cuối ngày chứ không phải đầu ngày hôm sau, để user mở app lúc 23:58
/// vẫn còn kịp giữ chuỗi.
/// </summary>
public class StreakCheckJob
{
    private readonly IGamificationRepository _gamificationRepository;

    public StreakCheckJob(IGamificationRepository gamificationRepository)
    {
        _gamificationRepository = gamificationRepository;
    }

    public Task RunAsync(CancellationToken ct = default)
        => ResetStaleStreaksAsync(VietnamTime.Today(), ct);

    public async Task ResetStaleStreaksAsync(DateOnly today, CancellationToken ct = default)
    {
        var profiles = await _gamificationRepository.GetProfilesWithStaleStreakAsync(today, ct);
        if (profiles.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var profile in profiles)
        {
            // longest_streak_days giữ nguyên — đó là kỷ lục, không phải trạng thái hiện tại.
            profile.CurrentStreakDays = 0;
            profile.UpdatedAt = now;
        }

        await _gamificationRepository.SaveChangesAsync(ct);
    }
}
