using System.Text.RegularExpressions;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

/// <summary>Không có trường nào về người đăng hay người báo cáo: nhân viên xử lý nội dung, không cần biết ai viết. Tên chỉ hiện khi tác giả đã chọn công khai.</summary>
public sealed record OpsCommunityReportDto(Guid Id, string TargetType, Guid TargetId, string Reason, string Priority, string? Title, string Body, string? PublicAuthorName, string TargetStatus, DateTimeOffset CreatedAt);

public sealed record OpsCommunityReportPageDto(IReadOnlyList<OpsCommunityReportDto> Items, int Total);

public sealed record OpsCommunityImageDto(Guid Id, Guid PostId, int Width, int Height, string? Title, string Body, DateTimeOffset CreatedAt);

public sealed record OpsCommunityImagePageDto(IReadOnlyList<OpsCommunityImageDto> Items, int Total);

public sealed record OpsResolveReportInput(string? Decision);

public sealed record OpsMuteAuthorInput(int Days);

public interface IOpsCommunityService
{
    Task<OpsCommunityReportPageDto> ListReportsAsync(Guid actorId, int offset, int limit, CancellationToken ct = default);
    Task ResolveReportAsync(Guid actorId, Guid reportId, OpsResolveReportInput input, string? ip, CancellationToken ct = default);
    Task<OpsCommunityImagePageDto> ListPendingImagesAsync(Guid actorId, int offset, int limit, CancellationToken ct = default);
    Task<CommunityImageContent> GetImageAsync(Guid actorId, Guid imageId, CancellationToken ct = default);
    Task ApproveImageAsync(Guid actorId, Guid imageId, string? ip, CancellationToken ct = default);
    Task RejectImageAsync(Guid actorId, Guid imageId, string? ip, CancellationToken ct = default);
    Task SetPostVisibleAsync(Guid actorId, Guid postId, bool visible, string? ip, CancellationToken ct = default);
    Task SetCommentVisibleAsync(Guid actorId, Guid commentId, bool visible, string? ip, CancellationToken ct = default);
    Task HidePlaceAsync(Guid actorId, Guid placeId, string? ip, CancellationToken ct = default);
    Task MutePostAuthorAsync(Guid actorId, Guid postId, OpsMuteAuthorInput input, string? ip, CancellationToken ct = default);
    Task MuteCommentAuthorAsync(Guid actorId, Guid commentId, OpsMuteAuthorInput input, string? ip, CancellationToken ct = default);
}

public sealed partial class OpsCommunityService : IOpsCommunityService
{
    private const string DecisionUpheld = "upheld";
    private const string DecisionDismissed = "dismissed";
    private const int MaxLimit = 200;
    private const int MaxMuteDays = 365;

    private readonly ICommunityRepository _community;
    private readonly IOpsAccessService _access;
    private readonly IAuditLogService _audit;

    public OpsCommunityService(ICommunityRepository community, IOpsAccessService access, IAuditLogService audit)
    {
        _community = community;
        _access = access;
        _audit = audit;
    }

    [GeneratedRegex("(?<!^)([A-Z])", RegexOptions.CultureInvariant, 200)]
    private static partial Regex WordStart();

    private static string Snake<T>(T value)
        where T : struct, Enum
        => WordStart().Replace(value.ToString(), "_$1").ToLowerInvariant();

    private static int Clamp(int limit) => limit <= 0 ? 50 : Math.Min(limit, MaxLimit);

    public async Task<OpsCommunityReportPageDto> ListReportsAsync(Guid actorId, int offset, int limit, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        var (rows, total) = await _community.ListOpenReportsAsync(Math.Max(0, offset), Clamp(limit), ct);
        var items = rows.Select(r => new OpsCommunityReportDto(
            r.Report.Id,
            r.Report.PostId is null ? "comment" : "post",
            r.Report.PostId ?? r.Report.CommentId!.Value,
            Snake(r.Report.Reason),
            Snake(r.Report.Priority),
            r.Title,
            r.Body,
            CommunityAuthorView.From(r.IsAnonymous, r.DisplayName).DisplayName,
            Snake(r.TargetStatus),
            r.Report.CreatedAt)).ToList();
        return new OpsCommunityReportPageDto(items, total);
    }

    /// <summary>Quyết định áp cho nội dung nên đóng luôn mọi báo cáo còn mở của nội dung đó. Giữ lại thì không ẩn gì, kể cả lúc nội dung đang bị tự ẩn.</summary>
    public async Task ResolveReportAsync(Guid actorId, Guid reportId, OpsResolveReportInput input, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        if (input.Decision is not (DecisionUpheld or DecisionDismissed))
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Quyết định phải là upheld hoặc dismissed.");
        }

        var report = await _community.GetReportAsync(reportId, ct) ?? throw new NotFoundException("CommunityReport", reportId);
        if (report.Status != CommunityReportStatus.Open)
        {
            throw new ConflictException(CommunityErrorCodes.AlreadyResolved, "Báo cáo này đã được xử lý.");
        }

        var upheld = input.Decision == DecisionUpheld;
        var status = upheld ? CommunityContentStatus.Removed : CommunityContentStatus.Visible;
        var now = DateTimeOffset.UtcNow;
        var outcome = upheld ? CommunityReportStatus.Upheld : CommunityReportStatus.Dismissed;
        if (report.PostId is { } postId)
        {
            await _community.SetPostStatusAsync(postId, status, ct);
            await _community.ResolveReportsOfPostAsync(postId, outcome, actor.Id, now, ct);
        }
        else if (report.CommentId is { } commentId)
        {
            await _community.SetCommentStatusAsync(commentId, status, ct);
            await _community.ResolveReportsOfCommentAsync(commentId, outcome, actor.Id, now, ct);
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityReportResolved, actor.Id, ip, new { reportId, decision = input.Decision }, ct);
    }

    public async Task<OpsCommunityImagePageDto> ListPendingImagesAsync(Guid actorId, int offset, int limit, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        var (rows, total) = await _community.ListPendingImagesAsync(Math.Max(0, offset), Clamp(limit), ct);
        return new OpsCommunityImagePageDto(rows.Select(r => new OpsCommunityImageDto(r.Id, r.PostId, r.Width, r.Height, r.Title, r.Body, r.CreatedAt)).ToList(), total);
    }

    public async Task<CommunityImageContent> GetImageAsync(Guid actorId, Guid imageId, CancellationToken ct = default)
    {
        await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        var image = await _community.GetImageAsync(imageId, ct);
        return image is { Data.Length: > 0 } ? new CommunityImageContent(image.Data) : throw new NotFoundException("CommunityPostImage", imageId);
    }

    public async Task ApproveImageAsync(Guid actorId, Guid imageId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        if (!await _community.ReviewImageAsync(imageId, approve: true, actor.Id, DateTimeOffset.UtcNow, ct))
        {
            throw new ConflictException(CommunityErrorCodes.AlreadyResolved, "Ảnh này đã được xử lý.");
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityImageApproved, actor.Id, ip, new { imageId }, ct);
    }

    /// <summary>Dùng được cả với ảnh đã duyệt, khi sau đó mới phát hiện vi phạm. Dữ liệu ảnh bị xóa ngay.</summary>
    public async Task RejectImageAsync(Guid actorId, Guid imageId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        if (!await _community.ReviewImageAsync(imageId, approve: false, actor.Id, DateTimeOffset.UtcNow, ct))
        {
            throw new ConflictException(CommunityErrorCodes.AlreadyResolved, "Ảnh này đã được xử lý.");
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityImageRejected, actor.Id, ip, new { imageId }, ct);
    }

    public async Task SetPostVisibleAsync(Guid actorId, Guid postId, bool visible, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        var status = visible ? CommunityContentStatus.Visible : CommunityContentStatus.Removed;
        if (!await _community.SetPostStatusAsync(postId, status, ct))
        {
            throw new NotFoundException("CommunityPost", postId);
        }

        var outcome = visible ? CommunityReportStatus.Dismissed : CommunityReportStatus.Upheld;
        await _community.ResolveReportsOfPostAsync(postId, outcome, actor.Id, DateTimeOffset.UtcNow, ct);
        await _audit.LogAsync(visible ? AuditEvents.OpsCommunityContentRestored : AuditEvents.OpsCommunityContentHidden, actor.Id, ip, new { postId }, ct);
    }

    public async Task SetCommentVisibleAsync(Guid actorId, Guid commentId, bool visible, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        var status = visible ? CommunityContentStatus.Visible : CommunityContentStatus.Removed;
        if (!await _community.SetCommentStatusAsync(commentId, status, ct))
        {
            throw new NotFoundException("CommunityComment", commentId);
        }

        var outcome = visible ? CommunityReportStatus.Dismissed : CommunityReportStatus.Upheld;
        await _community.ResolveReportsOfCommentAsync(commentId, outcome, actor.Id, DateTimeOffset.UtcNow, ct);
        await _audit.LogAsync(visible ? AuditEvents.OpsCommunityContentRestored : AuditEvents.OpsCommunityContentHidden, actor.Id, ip, new { commentId }, ct);
    }

    public async Task HidePlaceAsync(Guid actorId, Guid placeId, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.ModerateCommunity, ct);
        if (!await _community.HidePlaceAsync(placeId, DateTimeOffset.UtcNow, ct))
        {
            throw new NotFoundException("CommunityPlace", placeId);
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityPlaceHidden, actor.Id, ip, new { placeId }, ct);
    }

    /// <summary>Cấm theo nội dung chứ không theo tài khoản, nên người xử lý không biết tác giả là ai. Audit cũng chỉ ghi nội dung.</summary>
    public async Task MutePostAuthorAsync(Guid actorId, Guid postId, OpsMuteAuthorInput input, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.MuteCommunityAuthors, ct);
        var until = MuteUntil(input);
        if (!await _community.MuteAuthorOfPostAsync(postId, until, ct))
        {
            throw new NotFoundException("CommunityPost", postId);
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityAuthorMuted, actor.Id, ip, new { postId, input.Days }, ct);
    }

    public async Task MuteCommentAuthorAsync(Guid actorId, Guid commentId, OpsMuteAuthorInput input, string? ip, CancellationToken ct = default)
    {
        var actor = await _access.RequireAsync(actorId, OpsCapability.MuteCommunityAuthors, ct);
        var until = MuteUntil(input);
        if (!await _community.MuteAuthorOfCommentAsync(commentId, until, ct))
        {
            throw new NotFoundException("CommunityComment", commentId);
        }

        await _audit.LogAsync(AuditEvents.OpsCommunityAuthorMuted, actor.Id, ip, new { commentId, input.Days }, ct);
    }

    private static DateTimeOffset MuteUntil(OpsMuteAuthorInput input)
        => input.Days is >= 1 and <= MaxMuteDays
            ? DateTimeOffset.UtcNow.AddDays(input.Days)
            : throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, $"Số ngày cấm phải từ 1 đến {MaxMuteDays}.");
}
