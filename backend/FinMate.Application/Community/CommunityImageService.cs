using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

/// <summary>Kết quả vẽ lại ảnh thành JPEG. Không còn EXIF và không còn nội dung nào của tệp gốc ngoài các điểm ảnh.</summary>
public sealed record CommunityProcessedImage(byte[] Data, int Width, int Height);

public interface ICommunityImageProcessor
{
    /// <summary>Null khi tệp không phải JPEG, PNG, WebP đọc được, hoặc quá lớn để giải mã. Không ném lỗi với tệp xấu.</summary>
    Task<CommunityProcessedImage?> ProcessAsync(byte[] content, CancellationToken ct = default);
}

public sealed record CommunityImageContent(byte[] Data);

public interface ICommunityImageService
{
    Task<CommunityImageDto> UploadAsync(Guid userId, Guid postId, byte[] content, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid postId, Guid imageId, CancellationToken ct = default);
    Task<CommunityImageContent> GetAsync(Guid viewerId, Guid imageId, CancellationToken ct = default);
}

public sealed class CommunityImageService : ICommunityImageService
{
    public const int MaxUploadBytes = 5 * 1024 * 1024;
    private const int MaxImagesPerPost = 3;

    private readonly ICommunityRepository _community;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;
    private readonly ICommunityImageProcessor _processor;

    public CommunityImageService(ICommunityRepository community, IUserRepository users, ISystemConfigProvider config, ICommunityImageProcessor processor)
    {
        _community = community;
        _users = users;
        _config = config;
        _processor = processor;
    }

    public async Task<CommunityImageDto> UploadAsync(Guid userId, Guid postId, byte[] content, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Community;
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        CommunityRules.EnsureCanPost(user);

        var post = await _community.GetOwnPostAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        if (post.Status != CommunityContentStatus.Visible)
        {
            throw new BusinessRuleException(CommunityErrorCodes.PostNotEditable, "Bài này đang bị ẩn nên chưa thêm ảnh được.");
        }

        var existing = await _community.ListImagesAsync([postId], ct);
        await EnsureWithinLimitsAsync(userId, existing, settings, ct);

        if (content.Length is 0 or > MaxUploadBytes)
        {
            throw new BusinessRuleException(CommunityErrorCodes.ImageInvalid, "Ảnh phải có dữ liệu và không quá 5 MB.");
        }

        var processed = await _processor.ProcessAsync(content, ct)
            ?? throw new BusinessRuleException(CommunityErrorCodes.ImageInvalid, "Chỉ nhận ảnh JPEG, PNG hoặc WebP đọc được, tối đa 40 megapixel.");

        var image = new CommunityPostImage
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            Data = processed.Data,
            Width = processed.Width,
            Height = processed.Height,
            SizeBytes = processed.Data.Length,
            Position = existing.Count == 0 ? 0 : existing.Max(i => i.Position) + 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _community.AddImage(image);
        await _community.SaveChangesAsync(ct);
        return new CommunityImageDto(image.Id, image.Width, image.Height, CommunityImageStatus.Pending.ToString().ToLowerInvariant());
    }

    private async Task EnsureWithinLimitsAsync(Guid userId, IReadOnlyList<CommunityImageInfo> existing, CommunitySettings settings, CancellationToken ct)
    {
        if (existing.Count(i => i.Status is CommunityImageStatus.Pending or CommunityImageStatus.Approved) >= MaxImagesPerPost)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, $"Mỗi bài có tối đa {MaxImagesPerPost} ảnh.");
        }

        if (await _community.CountImagesSinceAsync(userId, DateTimeOffset.UtcNow.AddDays(-1), ct) >= settings.MaxImagesPerDay)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, $"Mỗi ngày chỉ tải được {settings.MaxImagesPerDay} ảnh.");
        }

        // Trần chung: người duyệt có hạn, nên hàng đợi không được dài vô tận dù từng người vẫn trong giới hạn.
        if (await _community.CountPendingImagesAsync(ct) >= settings.MaxPendingImages)
        {
            throw new BusinessRuleException(CommunityErrorCodes.ImageQueueFull, "Hệ thống đang có quá nhiều ảnh chờ duyệt. Thử lại sau nhé.");
        }
    }

    public async Task DeleteAsync(Guid userId, Guid postId, Guid imageId, CancellationToken ct = default)
    {
        _ = await _community.GetOwnPostAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        var image = await _community.GetImageOfPostAsync(imageId, postId, ct) ?? throw new NotFoundException("CommunityPostImage", imageId);
        image.Data = [];
        image.SizeBytes = 0;
        image.Status = CommunityImageStatus.Deleted;
        await _community.SaveChangesAsync(ct);
    }

    /// <summary>Người khác chỉ tải được ảnh đã duyệt của bài họ được xem. Sai ở bất kỳ điều kiện nào cũng là 404, để không ai dò được ảnh đang chờ duyệt.</summary>
    public async Task<CommunityImageContent> GetAsync(Guid viewerId, Guid imageId, CancellationToken ct = default)
    {
        var image = await _community.GetImageAsync(imageId, ct) ?? throw new NotFoundException("CommunityPostImage", imageId);
        var row = await _community.GetPostRowAsync(image.PostId, viewerId, ct);
        var allowed = row is not null
            && image.Data.Length > 0
            && (row.Post.AuthorId == viewerId || image.Status == CommunityImageStatus.Approved);
        return allowed ? new CommunityImageContent(image.Data) : throw new NotFoundException("CommunityPostImage", imageId);
    }
}
