using FinMate.Domain.Entities.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public interface IGamificationRepository
{
    Task<UserGamification?> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Chỉ track entity mới, KHÔNG lưu — caller quyết định thời điểm SaveChanges.</summary>
    void AddProfile(UserGamification profile);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<UserGamification>> GetProfilesWithStaleStreakAsync(DateOnly today, CancellationToken ct = default);

    Task<List<MascotItem>> GetAllMascotItemsAsync(CancellationToken ct = default);
    Task<List<UserMascotItem>> GetOwnedMascotItemsAsync(Guid userId, CancellationToken ct = default);
    void AddOwnedMascotItem(UserMascotItem item);
}

public interface IMissionRepository
{
    Task<List<Mission>> GetActiveMissionsAsync(CancellationToken ct = default);
    Task<List<Mission>> GetActiveMissionsByConditionAsync(MissionConditionType conditionType, CancellationToken ct = default);
    Task<Mission?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<List<UserMission>> GetUserMissionsAsync(Guid userId, IReadOnlyCollection<Guid> missionIds, DateOnly periodStart, CancellationToken ct = default);
    Task<List<UserMission>> GetActiveUserMissionsAsync(Guid userId, DateOnly today, CancellationToken ct = default);
    Task<List<UserMission>> GetCompletedUserMissionsAsync(Guid userId, int limit, CancellationToken ct = default);

    /// <summary>Đã hoàn thành mission <paramref name="code"/> ở bất kỳ chu kỳ nào chưa.</summary>
    Task<bool> HasEverCompletedAsync(Guid userId, string code, CancellationToken ct = default);

    void AddUserMission(UserMission userMission);
    Task SaveChangesAsync(CancellationToken ct = default);
}
