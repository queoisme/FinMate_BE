using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Quán hoặc địa điểm do người dùng nêu. Không lưu ai đã tạo: quán là thông tin chung, không phải của riêng một người.</summary>
public class CommunityPlace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Tên đã bỏ dấu và gộp khoảng trắng, để "Quán A" và "quan a" là một quán.</summary>
    public string NameKey { get; set; } = string.Empty;
    public string ProvinceCode { get; set; } = string.Empty;

    /// <summary>Chuỗi rỗng khi quán chỉ biết tới cấp tỉnh. Không dùng NULL vì UNIQUE của Postgres coi hai NULL là khác nhau.</summary>
    public string AreaCode { get; set; } = string.Empty;
    public string? AddressText { get; set; }
    public DateTimeOffset? HiddenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Một bài trong cộng đồng. <see cref="AuthorId"/> không bao giờ được trả ra ngoài, kể cả với nhân viên kiểm duyệt.</summary>
public class CommunityPost
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public string? Title { get; set; }

    /// <summary>Có thể rỗng với bài về quán: chạm một lần chỉ tính vào điểm quán và không hiện ở bảng tin.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Chỉ bài có chủ đề gắn quán mới có. Bài khác không có khu vực để người quen khó nhận ra tác giả.</summary>
    public Guid? PlaceId { get; set; }
    public long? PriceCents { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsAnonymous { get; set; } = true;
    public bool SpentHereVerified { get; set; }
    public CommunityContentStatus Status { get; set; } = CommunityContentStatus.Visible;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Ảnh đã được vẽ lại thành JPEG. File gốc không được lưu.</summary>
public class CommunityPostImage
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public byte[] Data { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public int SizeBytes { get; set; }
    public int Position { get; set; }
    public CommunityImageStatus Status { get; set; } = CommunityImageStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CommunityComment
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; } = true;
    public CommunityContentStatus Status { get; set; } = CommunityContentStatus.Visible;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public class CommunityPostVote
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Báo cáo một bài hoặc một bình luận, đúng một trong hai.</summary>
public class CommunityReport
{
    public Guid Id { get; set; }
    public Guid? PostId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid ReporterId { get; set; }
    public CommunityReportReason Reason { get; set; }
    public CommunityReportPriority Priority { get; set; }
    public CommunityReportStatus Status { get; set; } = CommunityReportStatus.Open;
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
