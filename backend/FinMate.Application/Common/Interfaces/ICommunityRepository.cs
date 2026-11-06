using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record CommunityPostFilter(
    Guid ViewerId,
    string? Topic,
    string? PlacesTopic,
    string? ProvinceCode,
    string? AreaCode,
    string? Tag,
    Guid? PlaceId,
    bool Mine,
    bool ByVotes,
    DateTimeOffset? Since,
    DateTimeOffset? BeforeCreatedAt,
    Guid? BeforeId,
    int Offset,
    int Limit);

public sealed record CommunityPostRow(CommunityPost Post, CommunityPlace? Place, string AuthorDisplayName, int Votes, int Comments, bool VotedByViewer);

public sealed record CommunityImageInfo(Guid Id, Guid PostId, int Width, int Height, int Position, CommunityImageStatus Status);

public sealed record CommunityPlaceShareRow(CommunityPlace Place, long? PriceCents, bool SpentHereVerified, int Votes, DateTimeOffset CreatedAt, List<string> Tags);

public sealed record CommunityCommentRow(CommunityComment Comment, string AuthorDisplayName);

/// <summary>
/// Dữ liệu cộng đồng. Không truy vấn nào ở đây chạm tới giao dịch, ngân sách hay số dư. Đường đọc công khai chỉ trả bài còn hiển thị của tài khoản còn hoạt động.
/// </summary>
public interface ICommunityRepository
{
    void AddPlace(CommunityPlace place);
    void AddPost(CommunityPost post);

    Task<CommunityPlace?> GetPlaceAsync(Guid placeId, CancellationToken ct = default);
    Task<CommunityPlace?> FindPlaceAsync(string provinceCode, string areaCode, string nameKey, CancellationToken ct = default);
    Task<List<CommunityPlace>> FindPlacesByKeyAsync(string nameKey, string? provinceCode, int limit, CancellationToken ct = default);

    /// <summary>Luôn lọc theo tác giả: bài của người khác coi như không tồn tại.</summary>
    Task<CommunityPost?> GetOwnPostAsync(Guid postId, Guid authorId, CancellationToken ct = default);
    Task<CommunityPost?> GetOwnPostForPlaceAsync(Guid authorId, Guid placeId, CancellationToken ct = default);

    /// <summary>Đếm cả bài đã xóa: đăng rồi xóa không được hoàn lại lượt.</summary>
    Task<int> CountPostsSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default);

    Task<CommunityPostRow?> GetPostRowAsync(Guid postId, Guid viewerId, CancellationToken ct = default);
    Task<List<CommunityPostRow>> ListPostsAsync(CommunityPostFilter filter, CancellationToken ct = default);
    Task<List<CommunityImageInfo>> ListImagesAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct = default);
    Task<List<CommunityPlaceShareRow>> ListPlaceSharesAsync(string? provinceCode, string? areaCode, Guid? placeId, string? nameKeyContains, int limit, CancellationToken ct = default);

    Task<bool> HasVoteAsync(Guid postId, Guid userId, CancellationToken ct = default);
    Task<int> CountVotesSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Gọi lại nhiều lần vẫn chỉ có một dòng, kể cả hai lời gọi cùng lúc.</summary>
    Task AddVoteAsync(Guid postId, Guid userId, DateTimeOffset now, CancellationToken ct = default);
    Task RemoveVoteAsync(Guid postId, Guid userId, CancellationToken ct = default);

    void AddComment(CommunityComment comment);

    /// <summary>Cũ trước, mới sau, để đọc như một cuộc trò chuyện. Bỏ bình luận đã ẩn và bình luận của tài khoản bị khóa hoặc đang chờ xóa.</summary>
    Task<List<CommunityCommentRow>> ListCommentsAsync(Guid postId, Guid viewerId, DateTimeOffset? afterCreatedAt, Guid? afterId, int limit, CancellationToken ct = default);
    Task<CommunityComment?> GetOwnCommentAsync(Guid commentId, Guid authorId, CancellationToken ct = default);
    Task<int> CountCommentsSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default);
    Task CloseOpenReportsOfCommentAsync(Guid commentId, DateTimeOffset now, CancellationToken ct = default);

    void AddImage(CommunityPostImage image);

    /// <summary>Có đọc dữ liệu ảnh. Đường danh sách dùng <see cref="ListImagesAsync"/>, không đụng tới cột này.</summary>
    Task<CommunityPostImage?> GetImageAsync(Guid imageId, CancellationToken ct = default);
    Task<CommunityPostImage?> GetImageOfPostAsync(Guid imageId, Guid postId, CancellationToken ct = default);

    /// <summary>Đếm cả ảnh đã xóa: xóa rồi tải lại không được hoàn lượt.</summary>
    Task<int> CountImagesSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default);
    Task<int> CountPendingImagesAsync(CancellationToken ct = default);

    /// <summary>Xóa dữ liệu mọi ảnh của bài, giữ dòng để bộ đếm giới hạn vẫn thấy.</summary>
    Task WipeImagesOfPostAsync(Guid postId, CancellationToken ct = default);
    Task CloseOpenReportsOfPostAsync(Guid postId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>False khi vi phạm ràng buộc duy nhất (hai lời gọi tạo cùng quán hoặc cùng bài); mọi thay đổi chưa lưu bị bỏ, người gọi đọc lại rồi thử lại.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
