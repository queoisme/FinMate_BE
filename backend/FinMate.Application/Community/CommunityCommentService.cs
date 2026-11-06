using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

/// <summary>
/// Chỉ hai trường về tác giả: <c>Author</c> và <c>IsMine</c>. Cố ý không có nhãn "người đăng bài": nhãn đó nối một bình luận có tên với bài ẩn danh của cùng người.
/// </summary>
public sealed record CommunityCommentDto(Guid Id, string Body, CommunityAuthorDto Author, bool IsMine, DateOnly CreatedOn, bool Edited);

public sealed record CommunityCommentPageDto(IReadOnlyList<CommunityCommentDto> Items, string? NextCursor);

public sealed record CreateCommunityCommentInput(string? Body, bool? IsAnonymous);

/// <summary>Null là giữ nguyên.</summary>
public sealed record UpdateCommunityCommentInput(string? Body, bool? IsAnonymous);

public interface ICommunityCommentService
{
    Task<CommunityCommentPageDto> ListAsync(Guid viewerId, Guid postId, string? cursor, int limit, CancellationToken ct = default);
    Task<CommunityCommentDto> CreateAsync(Guid userId, Guid postId, CreateCommunityCommentInput input, CancellationToken ct = default);
    Task<CommunityCommentDto> UpdateAsync(Guid userId, Guid commentId, UpdateCommunityCommentInput input, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid commentId, CancellationToken ct = default);
}

public sealed class CommunityCommentService : ICommunityCommentService
{
    private const int MaxBodyLength = 300;
    private const int DefaultLimit = 30;
    private const int MaxLimit = 100;

    private readonly ICommunityRepository _community;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;

    public CommunityCommentService(ICommunityRepository community, IUserRepository users, ISystemConfigProvider config)
    {
        _community = community;
        _users = users;
        _config = config;
    }

    public async Task<CommunityCommentPageDto> ListAsync(Guid viewerId, Guid postId, string? cursor, int limit, CancellationToken ct = default)
    {
        _ = await _community.GetPostRowAsync(postId, viewerId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        var size = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
        var position = CommunityCursor.ReadPosition(cursor);
        var rows = await _community.ListCommentsAsync(postId, viewerId, position?.CreatedAt, position?.Id, size + 1, ct);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? CommunityCursor.FromPosition(page[^1].Comment.CreatedAt, page[^1].Comment.Id) : null;
        return new CommunityCommentPageDto(page.Select(r => ToDto(r, viewerId)).ToList(), next);
    }

    public async Task<CommunityCommentDto> CreateAsync(Guid userId, Guid postId, CreateCommunityCommentInput input, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Community;
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        CommunityRules.EnsureCanPost(user);

        var row = await _community.GetPostRowAsync(postId, userId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        await EnsureCommentableAsync(row, ct);
        var body = CleanBody(input.Body, settings);
        var isAnonymous = input.IsAnonymous ?? true;
        CommunityRules.EnsureDisplayNameAllowed(isAnonymous, user);

        if (await _community.CountCommentsSinceAsync(userId, DateTimeOffset.UtcNow.AddDays(-1), ct) >= settings.MaxCommentsPerDay)
        {
            throw new BusinessRuleException(CommunityErrorCodes.LimitReached, $"Mỗi ngày chỉ bình luận được {settings.MaxCommentsPerDay} lần.");
        }

        var now = DateTimeOffset.UtcNow;
        var comment = new CommunityComment { Id = Guid.NewGuid(), PostId = postId, AuthorId = userId, Body = body, IsAnonymous = isAnonymous, CreatedAt = now, UpdatedAt = now };
        _community.AddComment(comment);
        await _community.SaveChangesAsync(ct);
        return ToDto(new CommunityCommentRow(comment, user.DisplayName), userId);
    }

    public async Task<CommunityCommentDto> UpdateAsync(Guid userId, Guid commentId, UpdateCommunityCommentInput input, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Community;
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);
        CommunityRules.EnsureNotMuted(user);

        var comment = await _community.GetOwnCommentAsync(commentId, userId, ct) ?? throw new NotFoundException("CommunityComment", commentId);
        if (comment.Status != CommunityContentStatus.Visible)
        {
            throw new BusinessRuleException(CommunityErrorCodes.PostNotEditable, "Bình luận này đang bị ẩn nên chưa sửa được.");
        }

        var isAnonymous = input.IsAnonymous ?? comment.IsAnonymous;
        CommunityRules.EnsureDisplayNameAllowed(isAnonymous, user);

        var now = DateTimeOffset.UtcNow;
        comment.Body = input.Body is null ? comment.Body : CleanBody(input.Body, settings);
        comment.IsAnonymous = isAnonymous;
        comment.UpdatedAt = now;
        comment.EditedAt = now;
        await _community.SaveChangesAsync(ct);
        return ToDto(new CommunityCommentRow(comment, user.DisplayName), userId);
    }

    public async Task DeleteAsync(Guid userId, Guid commentId, CancellationToken ct = default)
    {
        var comment = await _community.GetOwnCommentAsync(commentId, userId, ct) ?? throw new NotFoundException("CommunityComment", commentId);
        var now = DateTimeOffset.UtcNow;
        comment.Body = string.Empty;
        comment.DeletedAt = now;
        comment.UpdatedAt = now;
        await _community.SaveChangesAsync(ct);
        await _community.CloseOpenReportsOfCommentAsync(comment.Id, now, ct);
    }

    /// <summary>Bài chạm-một-lần không hiện ở bảng tin nên không ai có chỗ để bình luận; bài đã bị ẩn thì không nhận thêm.</summary>
    private async Task EnsureCommentableAsync(CommunityPostRow row, CancellationToken ct)
    {
        var hasContent = row.Post.Body.Length > 0 || (await _community.ListImagesAsync([row.Post.Id], ct)).Any(i => i.Status == CommunityImageStatus.Approved);
        if (row.Post.Status != CommunityContentStatus.Visible || !hasContent)
        {
            throw new BusinessRuleException(CommunityErrorCodes.CommentsNotAllowed, "Bài này không nhận bình luận.");
        }
    }

    private static string CleanBody(string? body, CommunitySettings settings)
    {
        var clean = body?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > MaxBodyLength)
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, $"Bình luận cần có nội dung, tối đa {MaxBodyLength} ký tự.");
        }

        if (!CommunityContentPolicy.IsAllowed(clean, settings.BannedWords))
        {
            throw new BusinessRuleException(CommunityErrorCodes.ContentNotAllowed, "Bình luận có đường dẫn, thông tin liên hệ, số dài hoặc từ không được phép.");
        }

        return clean;
    }

    private static CommunityCommentDto ToDto(CommunityCommentRow row, Guid viewerId)
        => new(
            row.Comment.Id,
            row.Comment.Body,
            CommunityAuthorView.From(row.Comment.IsAnonymous, row.AuthorDisplayName),
            row.Comment.AuthorId == viewerId,
            VietnamTime.DateOf(row.Comment.CreatedAt),
            row.Comment.EditedAt is not null);
}
