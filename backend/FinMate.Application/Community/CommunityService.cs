using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

/// <summary>Đăng, sửa, xóa bài và vote. Mọi thao tác ghi đều lấy tác giả từ JWT; bài của người khác trả 404.</summary>
public interface ICommunityService
{
    Task<CommunityCreateResult> CreatePostAsync(Guid userId, CreateCommunityPostInput input, CancellationToken ct = default);
    Task<CommunityPostDto> UpdatePostAsync(Guid userId, Guid postId, UpdateCommunityPostInput input, CancellationToken ct = default);
    Task DeletePostAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<CommunityVoteDto> VoteAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<CommunityVoteDto> UnvoteAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<CommunityProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<CommunityProfileDto> UpdateProfileAsync(Guid userId, UpdateCommunityProfileInput input, CancellationToken ct = default);
}

public sealed class CommunityService : ICommunityService
{
    private const int MaxTitleLength = 120;
    private const int MaxBodyLength = 2000;
    private const int MaxPlaceNameLength = 120;
    private const int MaxAddressLength = 200;
    private const int MaxTags = 5;
    private const long MaxPriceCents = 100_000_000;

    private readonly ICommunityRepository _community;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;
    private readonly ISpentHereVerifier _verifier;
    private readonly ICommunityReadService _read;

    public CommunityService(ICommunityRepository community, IUserRepository users, ISystemConfigProvider config, ISpentHereVerifier verifier, ICommunityReadService read)
    {
        _community = community;
        _users = users;
        _config = config;
        _verifier = verifier;
        _read = read;
    }

    private async Task<CommunitySettings> SettingsAsync(CancellationToken ct) => (await _config.GetAsync(ct)).Document.Community;

    public async Task<CommunityCreateResult> CreatePostAsync(Guid userId, CreateCommunityPostInput input, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        CommunityRules.EnsureCanPost(user);

        var topic = settings.Topics.FirstOrDefault(t => t.Code == input.Topic)
            ?? throw new BusinessRuleException(CommunityErrorCodes.TopicUnknown, "Chủ đề không tồn tại.");
        var clean = CleanContent(input.Title, input.Body, input.Tags, topic, settings);
        var isAnonymous = input.IsAnonymous ?? true;
        EnsureDisplayNameAllowed(isAnonymous, user);

        if (!topic.RequiresPlace)
        {
            EnsureNoPlaceFields(input);
            return await CreateStoryAsync(userId, topic, clean, isAnonymous, settings, ct);
        }

        EnsurePriceValid(input.PriceCents);

        // Hai lời gọi cùng tạo một quán hoặc cùng một bài thì lời sau gặp ràng buộc duy nhất: đọc lại rồi sẽ thấy dòng vừa có và cập nhật nó.
        var first = await SharePlaceAsync(userId, topic, clean, isAnonymous, input, settings, ct);
        var result = first ?? await SharePlaceAsync(userId, topic, clean, isAnonymous, input, settings, ct);
        return result ?? throw new ConflictException(CommunityErrorCodes.Conflict, "Có thao tác khác đang chạy. Thử lại sau nhé.");
    }

    private async Task<CommunityCreateResult> CreateStoryAsync(Guid userId, CommunityTopic topic, CleanedContent clean, bool isAnonymous, CommunitySettings settings, CancellationToken ct)
    {
        if (clean.Body.Length == 0)
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Bài cần có nội dung.");
        }

        await EnsureBelowPostLimitAsync(userId, settings, ct);
        var post = NewPost(userId, topic.Code, clean, isAnonymous);
        _community.AddPost(post);
        await _community.SaveChangesAsync(ct);
        return new CommunityCreateResult(await _read.GetOwnAsync(userId, post.Id, ct), true);
    }

    private async Task<CommunityCreateResult?> SharePlaceAsync(Guid userId, CommunityTopic topic, CleanedContent clean, bool isAnonymous, CreateCommunityPostInput input, CommunitySettings settings, CancellationToken ct)
    {
        var place = await ResolvePlaceAsync(input, settings, ct);
        var verified = await _verifier.IsVerifiedAsync(userId, input.TransactionId, place.NameKey, settings.VerifiedLookbackDays, ct);
        var now = DateTimeOffset.UtcNow;

        var existing = await _community.GetOwnPostForPlaceAsync(userId, place.Id, ct);
        if (existing is not null)
        {
            EnsureEditable(existing);
            ApplyContent(existing, clean, isAnonymous, input.PriceCents, now);
            existing.SpentHereVerified |= verified;
            return await _community.TrySaveChangesAsync(ct) ? new CommunityCreateResult(await _read.GetOwnAsync(userId, existing.Id, ct), false) : null;
        }

        await EnsureBelowPostLimitAsync(userId, settings, ct);
        var post = NewPost(userId, topic.Code, clean, isAnonymous);
        post.PlaceId = place.Id;
        post.PriceCents = input.PriceCents;
        post.SpentHereVerified = verified;
        _community.AddPost(post);
        return await _community.TrySaveChangesAsync(ct) ? new CommunityCreateResult(await _read.GetOwnAsync(userId, post.Id, ct), true) : null;
    }

    private async Task<CommunityPlace> ResolvePlaceAsync(CreateCommunityPostInput input, CommunitySettings settings, CancellationToken ct)
    {
        if (input.PlaceId is { } placeId)
        {
            return await _community.GetPlaceAsync(placeId, ct) ?? throw new NotFoundException("CommunityPlace", placeId);
        }

        var name = input.PlaceName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new BusinessRuleException(CommunityErrorCodes.PlaceRequired, "Bài về quán cần có tên quán và khu vực.");
        }

        var (province, area) = CommunityRules.RequireRegion(input.ProvinceCode, input.AreaCode, settings);
        var key = CounterpartyKey.From(name);
        if (key is null || name.Length > MaxPlaceNameLength || !CommunityContentPolicy.IsAllowed(name, settings.BannedWords))
        {
            throw new BusinessRuleException(CommunityErrorCodes.ContentNotAllowed, "Tên quán không hợp lệ.");
        }

        var address = input.AddressText?.Trim();
        if (address is { Length: > MaxAddressLength } || !CommunityContentPolicy.IsAllowed(address, settings.BannedWords))
        {
            throw new BusinessRuleException(CommunityErrorCodes.ContentNotAllowed, "Địa chỉ không hợp lệ.");
        }

        var found = await _community.FindPlaceAsync(province, area, key, ct);
        if (found is not null)
        {
            return found.HiddenAt is null ? found : throw new BusinessRuleException(CommunityErrorCodes.PlaceNotAllowed, "Quán này không còn nhận chia sẻ.");
        }

        var now = DateTimeOffset.UtcNow;
        var place = new CommunityPlace { Id = Guid.NewGuid(), Name = name, NameKey = key, ProvinceCode = province, AreaCode = area, AddressText = string.IsNullOrEmpty(address) ? null : address, CreatedAt = now, UpdatedAt = now };
        _community.AddPlace(place);
        return place;
    }

    public async Task<CommunityPostDto> UpdatePostAsync(Guid userId, Guid postId, UpdateCommunityPostInput input, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        CommunityRules.EnsureNotMuted(user);

        var post = await _community.GetOwnPostAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        EnsureEditable(post);
        var topic = settings.Topics.FirstOrDefault(t => t.Code == post.Topic);
        var clean = CleanContent(input.Title ?? post.Title, input.Body ?? post.Body, input.Tags ?? post.Tags, topic, settings);

        // Chủ đề không có quán thì không gắn giá được; ràng buộc CHECK cũng chặn, nhưng ở đây trả lỗi dễ hiểu.
        if (post.PlaceId is null && input.PriceCents is not null)
        {
            throw new BusinessRuleException(CommunityErrorCodes.PlaceNotAllowed, "Chủ đề này không gắn giá.");
        }

        if (post.PlaceId is null && clean.Body.Length == 0)
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Bài cần có nội dung.");
        }

        if (input.PriceCents is not 0)
        {
            EnsurePriceValid(input.PriceCents);
        }

        var isAnonymous = input.IsAnonymous ?? post.IsAnonymous;
        EnsureDisplayNameAllowed(isAnonymous, user);

        var price = input.PriceCents switch { null => post.PriceCents, 0 => null, var value => value };
        ApplyContent(post, clean, isAnonymous, price, DateTimeOffset.UtcNow);
        await _community.SaveChangesAsync(ct);
        return await _read.GetOwnAsync(userId, post.Id, ct);
    }

    /// <summary>Xóa nội dung, giá và ảnh ngay. Giữ dòng để bộ đếm giới hạn vẫn thấy, và bình luận của người khác không mất lý do tồn tại khi có khiếu nại.</summary>
    public async Task DeletePostAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var post = await _community.GetOwnPostAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        var now = DateTimeOffset.UtcNow;
        post.DeletedAt = now;
        post.UpdatedAt = now;
        post.Title = null;
        post.Body = string.Empty;
        post.PriceCents = null;
        post.Tags = [];
        await _community.SaveChangesAsync(ct);
        await _community.RemoveImagesOfPostAsync(post.Id, ct);
        await _community.CloseOpenReportsOfPostAsync(post.Id, now, ct);
    }

    public async Task<CommunityVoteDto> VoteAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var row = await _community.GetPostRowAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        if (row.Post.AuthorId == userId)
        {
            throw new BusinessRuleException(CommunityErrorCodes.OwnPost, "Bạn không thể vote bài của chính mình.");
        }

        if (row.VotedByViewer)
        {
            return new CommunityVoteDto(row.Votes, true);
        }

        var since = DateTimeOffset.UtcNow.AddHours(-1);
        if (await _community.CountVotesSinceAsync(userId, since, ct) >= settings.MaxVotesPerHour)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, "Bạn đã vote quá nhiều trong một giờ. Thử lại sau nhé.");
        }

        await _community.AddVoteAsync(postId, userId, DateTimeOffset.UtcNow, ct);
        return await CurrentVoteAsync(userId, postId, ct);
    }

    public async Task<CommunityVoteDto> UnvoteAsync(Guid userId, Guid postId, CancellationToken ct = default)
    {
        _ = await _community.GetPostRowAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        await _community.RemoveVoteAsync(postId, userId, ct);
        return await CurrentVoteAsync(userId, postId, ct);
    }

    private async Task<CommunityVoteDto> CurrentVoteAsync(Guid userId, Guid postId, CancellationToken ct)
    {
        var row = await _community.GetPostRowAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        return new CommunityVoteDto(row.Votes, row.VotedByViewer);
    }

    public async Task<CommunityProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
        => ToProfile(await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId));

    public async Task<CommunityProfileDto> UpdateProfileAsync(Guid userId, UpdateCommunityProfileInput input, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        if (input.ProvinceCode is not null || input.AreaCode is not null)
        {
            var (province, area) = CommunityRules.RequireRegion(input.ProvinceCode ?? user.CommunityProvinceCode, input.AreaCode, await SettingsAsync(ct));
            user.CommunityProvinceCode = province;
            user.CommunityAreaCode = area.Length == 0 ? null : area;
        }

        user.CommunityPromptsEnabled = input.PromptsEnabled ?? user.CommunityPromptsEnabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _users.UpdateAsync(user, ct);
        return ToProfile(user);
    }

    private static CommunityProfileDto ToProfile(User user)
        => new(user.CommunityProvinceCode, user.CommunityAreaCode, user.CommunityPromptsEnabled, user.CommunityConsentGranted, user.CommunityMutedUntil);

    private sealed record CleanedContent(string? Title, string Body, List<string> Tags);

    private static CleanedContent CleanContent(string? title, string? body, IReadOnlyList<string>? tags, CommunityTopic? topic, CommunitySettings settings)
    {
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var cleanBody = body?.Trim() ?? string.Empty;
        if (cleanTitle is { Length: > MaxTitleLength } || cleanBody.Length > MaxBodyLength)
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, $"Tiêu đề tối đa {MaxTitleLength} ký tự, nội dung tối đa {MaxBodyLength} ký tự.");
        }

        if (!CommunityContentPolicy.IsAllowed(cleanTitle, settings.BannedWords) || !CommunityContentPolicy.IsAllowed(cleanBody, settings.BannedWords))
        {
            throw new BusinessRuleException(CommunityErrorCodes.ContentNotAllowed, "Nội dung có đường dẫn, thông tin liên hệ, số dài hoặc từ không được phép.");
        }

        var cleanTags = (tags ?? []).Distinct().ToList();
        if (cleanTags.Count > MaxTags || (topic is not null && cleanTags.Any(t => !topic.Tags.Contains(t))))
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Thẻ không hợp lệ.");
        }

        return new CleanedContent(cleanTitle, cleanBody, cleanTags);
    }

    private static CommunityPost NewPost(Guid userId, string topic, CleanedContent clean, bool isAnonymous)
    {
        var now = DateTimeOffset.UtcNow;
        return new CommunityPost { Id = Guid.NewGuid(), Topic = topic, AuthorId = userId, Title = clean.Title, Body = clean.Body, Tags = clean.Tags, IsAnonymous = isAnonymous, CreatedAt = now, UpdatedAt = now };
    }

    private static void ApplyContent(CommunityPost post, CleanedContent clean, bool isAnonymous, long? priceCents, DateTimeOffset now)
    {
        post.Title = clean.Title;
        post.Body = clean.Body;
        post.Tags = clean.Tags;
        post.IsAnonymous = isAnonymous;
        post.PriceCents = priceCents;
        post.UpdatedAt = now;
        post.EditedAt = now;
    }

    private static void EnsureNoPlaceFields(CreateCommunityPostInput input)
    {
        if (input.PlaceId is not null || input.PlaceName is not null || input.ProvinceCode is not null || input.AreaCode is not null
            || input.AddressText is not null || input.PriceCents is not null || input.TransactionId is not null)
        {
            throw new BusinessRuleException(CommunityErrorCodes.PlaceNotAllowed, "Chủ đề này không gắn quán, giá hay giao dịch.");
        }
    }

    private static void EnsurePriceValid(long? priceCents)
    {
        if (priceCents is <= 0 or > MaxPriceCents)
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Giá không hợp lệ.");
        }
    }

    private static void EnsureDisplayNameAllowed(bool isAnonymous, User user)
    {
        if (!isAnonymous && !CommunityContentPolicy.IsDisplayNameAllowed(user.DisplayName))
        {
            throw new BusinessRuleException(CommunityErrorCodes.DisplayNameNotAllowed, "Tên hiển thị của bạn có thông tin liên hệ nên chưa thể hiện công khai. Hãy đổi tên hoặc đăng ẩn danh.");
        }
    }

    /// <summary>Bài đã bị ẩn không sửa được: nếu không, tác giả sửa bài vi phạm rồi đợi người khác không thấy lý do báo cáo cũ.</summary>
    private static void EnsureEditable(CommunityPost post)
    {
        if (post.Status != CommunityContentStatus.Visible)
        {
            throw new BusinessRuleException(CommunityErrorCodes.PostNotEditable, "Bài này đang bị ẩn nên chưa sửa được.");
        }
    }

    private async Task EnsureBelowPostLimitAsync(Guid userId, CommunitySettings settings, CancellationToken ct)
    {
        if (await _community.CountPostsSinceAsync(userId, DateTimeOffset.UtcNow.AddDays(-1), ct) >= settings.MaxPostsPerDay)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, $"Mỗi ngày chỉ đăng được {settings.MaxPostsPerDay} bài.");
        }
    }
}
