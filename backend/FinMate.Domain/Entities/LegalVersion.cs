namespace FinMate.Domain.Entities;

public enum LegalVersionStatus
{
    Draft,
    Published,
}

/// <summary>Một mục của văn bản. <c>Body</c> là văn bản thuần: lưu và trả nguyên văn, client không được hiển thị như HTML.</summary>
public sealed record LegalSection(string Title, string Body);

/// <summary>
/// Một phiên bản của cặp Điều khoản sử dụng + Chính sách quyền riêng tư (hai văn bản được đồng ý cùng lúc nên chung một số phiên bản).
/// <c>Version</c> là null khi còn là bản nháp và được gán lúc xuất bản. Dòng đã xuất bản không bao giờ sửa; có tối đa một dòng nháp.
/// </summary>
public class LegalVersion
{
    public Guid Id { get; set; }
    public int? Version { get; set; }
    public LegalVersionStatus Status { get; set; }
    public List<LegalSection> TermsSections { get; set; } = [];
    public List<LegalSection> PrivacySections { get; set; } = [];

    /// <summary>Người soạn gần nhất. Không có khóa ngoại: dòng bản 4 do migration tạo nên không có người soạn.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>Cũng là concurrency token: hai người sửa hoặc xuất bản cùng một nháp thì người sau nhận 409.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
