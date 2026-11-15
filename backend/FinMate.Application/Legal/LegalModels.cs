using FluentValidation;

namespace FinMate.Application.Legal;

public enum LegalDocumentKind
{
    Terms,
    Privacy,
}

public sealed record LegalSectionDto(string Title, string Body);

/// <param name="UpdatedAt">Lúc xuất bản.</param>
public sealed record LegalDocumentDto(int Version, DateTimeOffset UpdatedAt, IReadOnlyList<LegalSectionDto> Sections);

/// <param name="IsDraft">False khi chưa có nháp và đây là bản hiện hành, để Manager có điểm xuất phát.</param>
/// <param name="UpdatedAt">Null khi chưa có nháp. Client gửi lại đúng giá trị này làm <c>BaseUpdatedAt</c> ở lần lưu kế tiếp.</param>
public sealed record LegalDraftDto(
    bool IsDraft, DateTimeOffset? UpdatedAt, IReadOnlyList<LegalSectionDto> TermsSections, IReadOnlyList<LegalSectionDto> PrivacySections);

public sealed record LegalVersionSummaryDto(int? Version, string Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, Guid? PublishedBy);

public sealed record PublishedLegalVersionDto(int Version, DateTimeOffset PublishedAt);

/// <param name="BaseUpdatedAt">Bắt buộc khi đã có nháp: phải bằng <c>UpdatedAt</c> mà client nhận được lần trước, nếu không là 409.</param>
public sealed record SaveLegalDraftInput(
    IReadOnlyList<LegalSectionDto>? TermsSections, IReadOnlyList<LegalSectionDto>? PrivacySections, DateTimeOffset? BaseUpdatedAt);

public sealed class LegalSectionDtoValidator : AbstractValidator<LegalSectionDto>
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 20_000;

    public LegalSectionDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(MaxBodyLength);
    }
}

public sealed class SaveLegalDraftInputValidator : AbstractValidator<SaveLegalDraftInput>
{
    public const int MaxSections = 50;

    public SaveLegalDraftInputValidator()
    {
        RuleFor(x => x.TermsSections).NotNull().Must(HaveValidCount).WithMessage($"Điều khoản phải có từ 1 đến {MaxSections} mục.");
        RuleFor(x => x.PrivacySections).NotNull().Must(HaveValidCount).WithMessage($"Chính sách quyền riêng tư phải có từ 1 đến {MaxSections} mục.");
        RuleForEach(x => x.TermsSections).NotNull().SetValidator(new LegalSectionDtoValidator());
        RuleForEach(x => x.PrivacySections).NotNull().SetValidator(new LegalSectionDtoValidator());
    }

    private static bool HaveValidCount(IReadOnlyList<LegalSectionDto>? sections) => sections is null || sections.Count is >= 1 and <= MaxSections;
}

public static class LegalErrorCodes
{
    /// <summary>Nháp đã bị người khác sửa hoặc xuất bản từ lúc client tải về (409).</summary>
    public const string DraftStale = "legal_draft_stale";

    /// <summary>Không có bản nháp để xuất bản (422).</summary>
    public const string NoDraft = "legal_no_draft";

    /// <summary>Nháp giống hệt bản hiện hành: xuất bản sẽ buộc mọi người đồng ý lại mà không có gì mới (422).</summary>
    public const string DraftUnchanged = "legal_draft_unchanged";
}
