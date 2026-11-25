using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Groups.Invitations;

/// <param name="MaskedEmail">Che phần tên: <c>a***@gmail.com</c>. Người tìm chỉ cần nhận ra, không cần biết địa chỉ đầy đủ.</param>
public sealed record UserSearchResultDto(Guid UserId, string DisplayName, string MaskedEmail, string FinMateId);

public sealed record GroupInviteSuggestionDto(Guid UserId, string DisplayName, string FinMateId, int SharedGroupCount);

/// <summary>
/// Giới hạn số lần dò người dùng của MỖI người gọi trong một cửa sổ, cả tìm kiếm lẫn mời theo FinMate ID. Đếm cả lần tìm không ra: chặn dò mới là mục đích.
/// Số lần và độ dài cửa sổ nằm trong cấu hình hệ thống (<c>groups.userSearchLimit</c>, <c>groups.userSearchWindowMinutes</c>).
/// </summary>
public interface IUserSearchThrottle
{
    /// <summary>Tính một lần dò; ném 429 <c>USER_SEARCH_RATE_LIMITED</c> khi đã hết lượt.</summary>
    Task ConsumeAsync(Guid userId, CancellationToken ct = default);
}

public sealed class UserSearchThrottle : IUserSearchThrottle
{
    private sealed record Window(int Count, DateTimeOffset StartedAt);

    private readonly ICacheService _cache;
    private readonly ISystemConfigProvider _config;

    public UserSearchThrottle(ICacheService cache, ISystemConfigProvider config)
    {
        _cache = cache;
        _config = config;
    }

    public async Task ConsumeAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Groups;
        var length = TimeSpan.FromMinutes(settings.UserSearchWindowMinutes);
        var now = DateTimeOffset.UtcNow;
        var key = $"user-search:{userId}";

        var window = await _cache.GetAsync<Window>(key, ct);
        if (window is null || window.StartedAt + length <= now)
        {
            window = new Window(0, now);
        }

        if (window.Count >= settings.UserSearchLimit)
        {
            throw new TooManyRequestsException("USER_SEARCH_RATE_LIMITED", "Bạn tìm người quá nhiều lần. Hãy thử lại sau.", window.StartedAt + length);
        }

        // ponytail: đọc rồi ghi, không nguyên tử: hai yêu cầu đồng thời có thể lọt thêm vài lượt. Đủ để chặn dò hàng loạt; cần chặt hơn thì chuyển sang INCR của Redis.
        await _cache.SetAsync(key, window with { Count = window.Count + 1 }, window.StartedAt + length - now, ct);
    }
}

public interface IUserSearchService
{
    /// <summary>Khớp CHÍNH XÁC theo email (tài khoản đã xác minh) hoặc FinMate ID; không tìm gần đúng. Số điện thoại chưa được hỗ trợ.</summary>
    Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(Guid userId, string? query, Guid? groupId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupInviteSuggestionDto>> SuggestAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

public class UserSearchService : IUserSearchService
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 320;

    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IUserSearchThrottle _throttle;
    private readonly ISystemConfigProvider _config;

    public UserSearchService(IUserRepository users, IGroupRepository groups, IGroupAccessService access, IUserSearchThrottle throttle, ISystemConfigProvider config)
    {
        _users = users;
        _groups = groups;
        _access = access;
        _throttle = throttle;
        _config = config;
    }

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(Guid userId, string? query, Guid? groupId, CancellationToken ct = default)
    {
        var q = query?.Trim() ?? string.Empty;
        if (q.Length < MinQueryLength || q.Length > MaxQueryLength)
        {
            throw new ValidationException([new ValidationFailure("q", $"Từ khóa phải từ {MinQueryLength} đến {MaxQueryLength} ký tự.")]);
        }

        if (groupId is { } gid)
        {
            await _access.RequireAsync(gid, userId, GroupPermission.InviteMembers, ct);
        }

        await _throttle.ConsumeAsync(userId, ct);

        User? found = null;
        if (q.Contains('@'))
        {
            // Chỉ tài khoản đã xác minh email: chưa xác minh thì người gõ có thể đang đoán địa chỉ của người khác.
            var byEmail = await _users.GetByEmailAsync(q.ToLowerInvariant(), ct);
            found = byEmail is { EmailVerifiedAt: not null } ? byEmail : null;
        }
        else if (FinMateIds.TryNormalize(q, out var finMateId))
        {
            found = await _users.GetByFinMateIdAsync(finMateId, ct);
        }

        if (found is null || found.Id == userId || found.IsLocked)
        {
            return [];
        }

        if (groupId is { } group && await _groups.GetActiveMemberAsync(group, found.Id, ct) is not null)
        {
            return [];   // đã trong nhóm: không có gì để mời
        }

        return [new UserSearchResultDto(found.Id, found.DisplayName, MaskEmail(found.Email), found.FinMateId)];
    }

    public async Task<IReadOnlyList<GroupInviteSuggestionDto>> SuggestAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.InviteMembers, ct);
        var max = (await _config.GetAsync(ct)).Document.Groups.MaxInviteSuggestions;
        return (await _groups.ListInviteCandidatesAsync(userId, groupId, DateTimeOffset.UtcNow, max, ct))
            .Select(c => new GroupInviteSuggestionDto(c.UserId, c.DisplayName, c.FinMateId, c.SharedGroupCount))
            .ToList();
    }

    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
