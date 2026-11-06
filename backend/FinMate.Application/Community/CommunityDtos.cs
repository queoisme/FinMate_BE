namespace FinMate.Application.Community;

public static class CommunityErrorCodes
{
    public const string Disabled = "COMMUNITY_DISABLED";
    public const string InvalidInput = "COMMUNITY_INVALID_INPUT";
    public const string ConsentRequired = "COMMUNITY_CONSENT_REQUIRED";
    public const string Muted = "COMMUNITY_MUTED";
    public const string TopicUnknown = "COMMUNITY_TOPIC_UNKNOWN";
    public const string RegionUnknown = "COMMUNITY_REGION_UNKNOWN";
    public const string PlaceRequired = "COMMUNITY_PLACE_REQUIRED";
    public const string PlaceNotAllowed = "COMMUNITY_PLACE_NOT_ALLOWED";
    public const string ContentNotAllowed = "COMMUNITY_CONTENT_NOT_ALLOWED";
    public const string DisplayNameNotAllowed = "COMMUNITY_DISPLAY_NAME_NOT_ALLOWED";
    public const string LimitReached = "COMMUNITY_LIMIT_REACHED";
    public const string OwnPost = "COMMUNITY_OWN_POST";
    public const string PostNotEditable = "COMMUNITY_POST_NOT_EDITABLE";
    public const string Conflict = "COMMUNITY_CONFLICT";
    public const string CommentsNotAllowed = "COMMUNITY_COMMENTS_NOT_ALLOWED";
    public const string ImageInvalid = "COMMUNITY_IMAGE_INVALID";
    public const string ImageQueueFull = "COMMUNITY_IMAGE_QUEUE_FULL";
    public const string AlreadyResolved = "COMMUNITY_ALREADY_RESOLVED";
}

/// <summary>Là thứ duy nhất nói về tác giả trong mọi response công khai. Cố ý không có id.</summary>
public sealed record CommunityAuthorDto(bool IsAnonymous, string? DisplayName);

public sealed record CommunityPlaceDto(Guid Id, string Name, string ProvinceCode, string AreaCode, string? AddressText);

public sealed record CommunityImageDto(Guid Id, int Width, int Height, string Status);

/// <summary>Chỉ có ngày, không có giờ: giờ đăng cộng với nội dung đủ để người quen đoán ra tác giả ẩn danh.</summary>
public sealed record CommunityPostDto(
    Guid Id,
    string Topic,
    string? Title,
    string Body,
    IReadOnlyList<string> Tags,
    CommunityAuthorDto Author,
    bool IsMine,
    DateOnly CreatedOn,
    bool Edited,
    int VoteCount,
    int CommentCount,
    bool VotedByMe,
    bool SpentHereVerified,
    long? PriceCents,
    CommunityPlaceDto? Place,
    IReadOnlyList<CommunityImageDto> Images,
    string? Status);

public sealed record CommunityPostPageDto(IReadOnlyList<CommunityPostDto> Items, string? NextCursor);

public sealed record CommunityVoteDto(int VoteCount, bool VotedByMe);

public sealed record CommunityPlaceSummaryDto(
    Guid Id,
    string Name,
    string ProvinceCode,
    string AreaCode,
    string? AddressText,
    int ShareCount,
    int SpentHereCount,
    int VoteCount,
    long? TypicalPriceCents,
    IReadOnlyList<string> Tags,
    DateOnly LastSharedOn);

public sealed record CommunityPlacePageDto(IReadOnlyList<CommunityPlaceSummaryDto> Items, string? NextCursor);

public sealed record CommunityPlaceDetailDto(CommunityPlaceSummaryDto Place, IReadOnlyList<CommunityPostDto> Posts);

public sealed record CommunityProfileDto(string? ProvinceCode, string? AreaCode, bool PromptsEnabled, bool ConsentGranted, DateTimeOffset? MutedUntil);

public sealed record CreateCommunityPostInput(
    string? Topic,
    string? Title,
    string? Body,
    bool? IsAnonymous,
    IReadOnlyList<string>? Tags,
    Guid? PlaceId,
    string? PlaceName,
    string? ProvinceCode,
    string? AreaCode,
    string? AddressText,
    long? PriceCents,
    Guid? TransactionId);

/// <summary>Null là giữ nguyên. <see cref="PriceCents"/> bằng 0 là bỏ giá.</summary>
public sealed record UpdateCommunityPostInput(string? Title, string? Body, bool? IsAnonymous, IReadOnlyList<string>? Tags, long? PriceCents);

public sealed record CommunityPostQuery(string? Topic, string? ProvinceCode, string? AreaCode, string? Tag, string? Sort, bool Mine, string? Cursor, int Limit);

public sealed record CommunityPlaceQuery(string? ProvinceCode, string? AreaCode, string? Tag, long? MaxPriceCents, string? Q, string? Cursor, int Limit);

public sealed record UpdateCommunityProfileInput(string? ProvinceCode, string? AreaCode, bool? PromptsEnabled);

public sealed record CommunityCreateResult(CommunityPostDto Post, bool Created);
