using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

public sealed record CommunityReportInput(string? Reason);

public interface ICommunityReportService
{
    Task ReportPostAsync(Guid userId, Guid postId, CommunityReportInput input, CancellationToken ct = default);
    Task ReportCommentAsync(Guid userId, Guid commentId, CommunityReportInput input, CancellationToken ct = default);
}

/// <summary>Báo cáo lần hai của cùng một người không tính thêm, nên một người không tự ẩn được bài của người khác.</summary>
public sealed class CommunityReportService : ICommunityReportService
{
    private readonly ICommunityRepository _community;
    private readonly ISystemConfigProvider _config;

    public CommunityReportService(ICommunityRepository community, ISystemConfigProvider config)
    {
        _community = community;
        _config = config;
    }

    public async Task ReportPostAsync(Guid userId, Guid postId, CommunityReportInput input, CancellationToken ct = default)
    {
        var reason = ParseReason(input.Reason);
        var row = await _community.GetPostRowAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        if (row.Post.AuthorId == userId)
        {
            throw new BusinessRuleException(CommunityErrorCodes.OwnPost, "Bạn không thể báo cáo nội dung của chính mình.");
        }

        if (await _community.HasReportedPostAsync(postId, userId, ct))
        {
            return;
        }

        var settings = await EnsureBelowLimitAsync(userId, ct);
        if (!await SaveAsync(new CommunityReport { PostId = postId }, userId, reason, ct))
        {
            return;
        }

        if (await _community.CountOpenHidingReportsOfPostAsync(postId, ct) >= settings.AutoHideReportCount)
        {
            await _community.AutoHidePostAsync(postId, ct);
        }
    }

    public async Task ReportCommentAsync(Guid userId, Guid commentId, CommunityReportInput input, CancellationToken ct = default)
    {
        var reason = ParseReason(input.Reason);
        var comment = await _community.GetVisibleCommentAsync(commentId, userId, ct) ?? throw new NotFoundException("CommunityComment", commentId);
        if (comment.AuthorId == userId)
        {
            throw new BusinessRuleException(CommunityErrorCodes.OwnPost, "Bạn không thể báo cáo nội dung của chính mình.");
        }

        if (await _community.HasReportedCommentAsync(commentId, userId, ct))
        {
            return;
        }

        var settings = await EnsureBelowLimitAsync(userId, ct);
        if (!await SaveAsync(new CommunityReport { CommentId = commentId }, userId, reason, ct))
        {
            return;
        }

        if (await _community.CountOpenHidingReportsOfCommentAsync(commentId, ct) >= settings.AutoHideReportCount)
        {
            await _community.AutoHideCommentAsync(commentId, ct);
        }
    }

    private async Task<CommunitySettings> EnsureBelowLimitAsync(Guid userId, CancellationToken ct)
    {
        var settings = (await _config.GetAsync(ct)).Document.Community;
        if (await _community.CountReportsSinceAsync(userId, DateTimeOffset.UtcNow.AddDays(-1), ct) >= settings.MaxReportsPerDay)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, $"Mỗi ngày chỉ gửi được {settings.MaxReportsPerDay} báo cáo.");
        }

        return settings;
    }

    private Task<bool> SaveAsync(CommunityReport report, Guid userId, CommunityReportReason reason, CancellationToken ct)
    {
        report.Id = Guid.NewGuid();
        report.ReporterId = userId;
        report.Reason = reason;
        report.Priority = reason == CommunityReportReason.SelfHarm ? CommunityReportPriority.Urgent : CommunityReportPriority.Normal;
        report.CreatedAt = DateTimeOffset.UtcNow;
        _community.AddReport(report);
        return _community.TrySaveChangesAsync(ct);  // false: lời gọi song song của chính người này đã ghi trước
    }

    private static CommunityReportReason ParseReason(string? reason)
        => reason switch
        {
            "scam" => CommunityReportReason.Scam,
            "personal_info" => CommunityReportReason.PersonalInfo,
            "harassment" => CommunityReportReason.Harassment,
            "sexual" => CommunityReportReason.Sexual,
            "spam" => CommunityReportReason.Spam,
            "self_harm" => CommunityReportReason.SelfHarm,
            "other" => CommunityReportReason.Other,
            _ => throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Lý do báo cáo không hợp lệ."),
        };
}
