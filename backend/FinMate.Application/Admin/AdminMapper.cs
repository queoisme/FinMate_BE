using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Entities.Gamification;

namespace FinMate.Application.Admin;

public static class AdminMapper
{
    /// <summary>
    /// Ánh xạ tường minh, không dùng AutoMapper cho DTO này có chủ ý: đây là chỗ duy nhất
    /// quyết định admin thấy trường nào của <see cref="User"/>. Một profile mapping tự động
    /// sẽ lặng lẽ kéo theo trường mới mỗi khi entity mọc thêm cột.
    /// </summary>
    public static AdminUserDto ToDto(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.IsLocked,
        user.PasswordHash is not null,
        user.GoogleId is not null,
        user.CreatedAt,
        user.DeletedAt);

    public static ProviderConfigDto ToDto(ProviderConfig config) => new(
        config.Id,
        config.ProviderKey,
        config.DisplayName,
        config.PackageName,
        config.AccountType,
        config.IsActive,
        config.CreatedAt,
        config.UpdatedAt);

    public static AdminCategoryDto ToDto(Category category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.IconName,
        category.IsActive,
        category.CreatedAt,
        category.UpdatedAt);

    public static AdminMissionDto ToDto(Mission mission) => new(
        mission.Id,
        mission.Code,
        mission.Title,
        mission.Description,
        mission.PeriodType,
        mission.ConditionType,
        mission.ConditionTarget,
        mission.ExpReward,
        mission.IsActive,
        mission.CreatedAt,
        mission.UpdatedAt);
}
