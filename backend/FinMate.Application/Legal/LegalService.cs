using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Legal;

public interface ILegalRepository
{
    /// <summary>Số phiên bản đã xuất bản lớn nhất; null nếu chưa có (không xảy ra sau migration).</summary>
    Task<int?> GetCurrentVersionAsync(CancellationToken ct = default);

    Task<LegalVersion?> GetCurrentPublishedAsync(CancellationToken ct = default);

    /// <summary>Có theo dõi thay đổi, để sửa hoặc xuất bản.</summary>
    Task<LegalVersion?> GetDraftAsync(CancellationToken ct = default);

    /// <summary>Mới nhất trước.</summary>
    Task<IReadOnlyList<LegalVersion>> ListAsync(CancellationToken ct = default);

    void Add(LegalVersion version);

    /// <summary>Ném <c>ConflictException(LegalErrorCodes.DraftStale)</c> khi dòng đã bị người khác đổi.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Thay cho hằng <c>ConsentVersions.Terms</c>: phiên bản Điều khoản hiện hành là dòng đã xuất bản mới nhất.</summary>
public interface ILegalVersionProvider
{
    Task<int> CurrentTermsVersionAsync(CancellationToken ct = default);
}

/// <summary>Đọc công khai. Chỉ bao giờ trả bản đã xuất bản, không bao giờ trả nháp.</summary>
public interface ILegalDocumentService
{
    Task<LegalDocumentDto> GetPublishedAsync(LegalDocumentKind kind, CancellationToken ct = default);
}

public sealed class LegalDocumentService : ILegalDocumentService
{
    private readonly ILegalRepository _repository;

    public LegalDocumentService(ILegalRepository repository)
    {
        _repository = repository;
    }

    public async Task<LegalDocumentDto> GetPublishedAsync(LegalDocumentKind kind, CancellationToken ct = default)
    {
        var current = await _repository.GetCurrentPublishedAsync(ct)
            ?? throw new NotFoundException("LegalDocument", kind.ToString());
        var sections = kind == LegalDocumentKind.Terms ? current.TermsSections : current.PrivacySections;
        return new LegalDocumentDto(
            current.Version!.Value,
            current.PublishedAt ?? current.UpdatedAt,
            sections.Select(s => new LegalSectionDto(s.Title, s.Body)).ToList());
    }
}

public sealed class LegalVersionProvider : ILegalVersionProvider
{
    private readonly ILegalRepository _repository;

    public LegalVersionProvider(ILegalRepository repository)
    {
        _repository = repository;
    }

    // Không cache: mỗi lần xuất bản phải có hiệu lực ngay ở mọi instance.
    public async Task<int> CurrentTermsVersionAsync(CancellationToken ct = default)
        => await _repository.GetCurrentVersionAsync(ct)
            ?? throw new InvalidOperationException("Chưa có phiên bản Điều khoản nào được xuất bản; migration AddLegalVersions phải tạo bản 4.");
}

/// <summary>Soạn nháp và xem lịch sử (Manager và Admin). Mỗi lần gọi đọc lại quyền từ DB qua <c>IOpsAccessService</c>.</summary>
public interface ILegalAdminService
{
    Task<IReadOnlyList<LegalVersionSummaryDto>> ListVersionsAsync(Guid actorId, CancellationToken ct = default);

    /// <summary>Nháp nếu có; không thì bản hiện hành (<c>IsDraft = false</c>) làm điểm xuất phát.</summary>
    Task<LegalDraftDto> GetDraftAsync(Guid actorId, CancellationToken ct = default);

    /// <summary>Tạo hoặc ghi đè nháp duy nhất. 409 <c>legal_draft_stale</c> nếu đã có nháp và <c>BaseUpdatedAt</c> không khớp.</summary>
    Task<LegalDraftDto> SaveDraftAsync(Guid actorId, SaveLegalDraftInput input, string? ip, CancellationToken ct = default);

    /// <summary>
    /// Chỉ Admin (<c>ManageSystem</c>). Biến nháp thành phiên bản mới = số lớn nhất đã xuất bản + 1, và mọi đồng ý ở phiên bản cũ mất hiệu lực.
    /// 422 <c>legal_no_draft</c> nếu không có nháp, 422 <c>legal_draft_unchanged</c> nếu nháp giống hệt bản hiện hành,
    /// 409 <c>legal_draft_stale</c> nếu người khác vừa xuất bản hoặc sửa nháp.
    /// </summary>
    Task<PublishedLegalVersionDto> PublishAsync(Guid actorId, string? ip, CancellationToken ct = default);
}

public sealed class LegalAdminService : ILegalAdminService
{
    private readonly IOpsAccessService _access;
    private readonly ILegalRepository _repository;
    private readonly IAuditLogService _audit;
    private readonly IValidator<SaveLegalDraftInput> _validator;

    public LegalAdminService(
        IOpsAccessService access, ILegalRepository repository, IAuditLogService audit, IValidator<SaveLegalDraftInput> validator)
    {
        _access = access;
        _repository = repository;
        _audit = audit;
        _validator = validator;
    }

    public async Task<IReadOnlyList<LegalVersionSummaryDto>> ListVersionsAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageLegalDocuments, ct);
        return (await _repository.ListAsync(ct))
            .Select(v => new LegalVersionSummaryDto(v.Version, v.Status.ToString().ToLowerInvariant(), v.UpdatedAt, v.PublishedAt, v.PublishedBy))
            .ToList();
    }

    public async Task<LegalDraftDto> GetDraftAsync(Guid actorId, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageLegalDocuments, ct);
        var draft = await _repository.GetDraftAsync(ct);
        if (draft is not null)
        {
            return ToDto(draft, isDraft: true);
        }

        var current = await _repository.GetCurrentPublishedAsync(ct)
            ?? throw new NotFoundException("LegalDocument", "current");
        return ToDto(current, isDraft: false);
    }

    public async Task<LegalDraftDto> SaveDraftAsync(Guid actorId, SaveLegalDraftInput input, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageLegalDocuments, ct);
        await _validator.ValidateAndThrowAsync(input, ct);

        var draft = await _repository.GetDraftAsync(ct);
        if (draft is null)
        {
            draft = new LegalVersion { Id = Guid.NewGuid(), Status = LegalVersionStatus.Draft };
            _repository.Add(draft);
        }
        else if (input.BaseUpdatedAt != draft.UpdatedAt)
        {
            throw new ConflictException(LegalErrorCodes.DraftStale, "Bản nháp vừa được người khác thay đổi. Hãy tải lại rồi sửa tiếp.");
        }

        draft.TermsSections = input.TermsSections!.Select(s => new LegalSection(s.Title, s.Body)).ToList();
        draft.PrivacySections = input.PrivacySections!.Select(s => new LegalSection(s.Title, s.Body)).ToList();
        draft.CreatedBy = actorId;
        draft.UpdatedAt = NowAtDatabasePrecision();
        await _repository.SaveChangesAsync(ct);

        // Chỉ ghi id nháp: nội dung dài và đã nằm ở chính bảng legal_versions.
        await _audit.LogAsync(AuditEvents.LegalDraftSaved, actorId, ip, new { draftId = draft.Id }, ct);
        return ToDto(draft, isDraft: true);
    }

    public async Task<PublishedLegalVersionDto> PublishAsync(Guid actorId, string? ip, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ManageSystem, ct);

        var draft = await _repository.GetDraftAsync(ct)
            ?? throw new BusinessRuleException(LegalErrorCodes.NoDraft, "Chưa có bản nháp để xuất bản.");
        var current = await _repository.GetCurrentPublishedAsync(ct);
        if (current is not null
            && current.TermsSections.SequenceEqual(draft.TermsSections)
            && current.PrivacySections.SequenceEqual(draft.PrivacySections))
        {
            throw new BusinessRuleException(LegalErrorCodes.DraftUnchanged,
                "Bản nháp giống hệt bản đang hiệu lực. Xuất bản sẽ buộc mọi người dùng đồng ý lại mà không có gì mới.");
        }

        var now = NowAtDatabasePrecision();
        draft.Version = (current?.Version ?? 0) + 1;
        draft.Status = LegalVersionStatus.Published;
        draft.PublishedBy = actorId;
        draft.PublishedAt = now;

        // UpdatedAt là concurrency token: hai Admin bấm cùng lúc thì chỉ một lần ghi thắng, người kia nhận 409 từ repository.
        draft.UpdatedAt = now;
        await _repository.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditEvents.LegalVersionPublished, actorId, ip, new { version = draft.Version }, ct);
        return new PublishedLegalVersionDto(draft.Version.Value, now);
    }

    /// <summary>
    /// Postgres lưu <c>timestamptz</c> tới micro giây còn .NET giữ 100ns. Trả giá trị chưa cắt thì lần lưu kế tiếp client gửi lại nó,
    /// không bằng giá trị đã đọc từ DB và bị 409 oan.
    /// </summary>
    internal static DateTimeOffset NowAtDatabasePrecision()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static LegalDraftDto ToDto(LegalVersion version, bool isDraft) => new(
        isDraft,
        isDraft ? version.UpdatedAt : null,
        version.TermsSections.Select(s => new LegalSectionDto(s.Title, s.Body)).ToList(),
        version.PrivacySections.Select(s => new LegalSectionDto(s.Title, s.Body)).ToList());
}
