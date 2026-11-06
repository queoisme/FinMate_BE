using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class CommunityRepository : ICommunityRepository
{
    private readonly FinMateDbContext _context;

    public CommunityRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public void AddPlace(CommunityPlace place) => _context.CommunityPlaces.Add(place);

    public void AddPost(CommunityPost post) => _context.CommunityPosts.Add(post);

    public Task<CommunityPlace?> GetPlaceAsync(Guid placeId, CancellationToken ct = default)
        => _context.CommunityPlaces.AsNoTracking().FirstOrDefaultAsync(p => p.Id == placeId && p.HiddenAt == null, ct);

    public Task<CommunityPlace?> FindPlaceAsync(string provinceCode, string areaCode, string nameKey, CancellationToken ct = default)
        => _context.CommunityPlaces.AsNoTracking().FirstOrDefaultAsync(p => p.ProvinceCode == provinceCode && p.AreaCode == areaCode && p.NameKey == nameKey, ct);

    public Task<List<CommunityPlace>> FindPlacesByKeyAsync(string nameKey, string? provinceCode, int limit, CancellationToken ct = default)
        => _context.CommunityPlaces.AsNoTracking()
            .Where(p => p.NameKey == nameKey && p.HiddenAt == null && (provinceCode == null || p.ProvinceCode == provinceCode))
            .OrderBy(p => p.AreaCode)
            .Take(limit)
            .ToListAsync(ct);

    public Task<CommunityPost?> GetOwnPostAsync(Guid postId, Guid authorId, CancellationToken ct = default)
        => _context.CommunityPosts.FirstOrDefaultAsync(p => p.Id == postId && p.AuthorId == authorId, ct);

    public Task<CommunityPost?> GetOwnPostForPlaceAsync(Guid authorId, Guid placeId, CancellationToken ct = default)
        => _context.CommunityPosts.FirstOrDefaultAsync(p => p.AuthorId == authorId && p.PlaceId == placeId, ct);

    public Task<bool> HasOwnPostForPlaceKeyAsync(Guid authorId, string nameKey, CancellationToken ct = default)
        => (from post in _context.CommunityPosts
            join place in _context.CommunityPlaces on post.PlaceId equals place.Id
            where post.AuthorId == authorId && place.NameKey == nameKey
            select post.Id).AnyAsync(ct);

    public Task<int> CountPostsSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default)
        => _context.CommunityPosts.IgnoreQueryFilters().CountAsync(p => p.AuthorId == authorId && p.CreatedAt >= since, ct);

    // Lớp có thuộc tính init chứ không phải record có constructor: EF chỉ dịch được Where và OrderBy sau Select nếu dựng bằng khởi tạo thành viên.
    private sealed class JoinedPost
    {
        public required CommunityPost Post { get; init; }
        public CommunityPlace? Place { get; init; }
        public required string DisplayName { get; init; }
    }

    private sealed class PostProjection
    {
        public required CommunityPost Post { get; init; }
        public CommunityPlace? Place { get; init; }
        public required string DisplayName { get; init; }
        public int Votes { get; init; }
        public int Comments { get; init; }
        public bool VotedByViewer { get; init; }

        public CommunityPostRow ToRow() => new(Post, Place, DisplayName, Votes, Comments, VotedByViewer);
    }

    /// <summary>
    /// Join <c>Users</c> có query filter xóa mềm, nên bài của người đang chờ xóa tự biến mất; khôi phục tài khoản thì bài trở lại, không phải sửa gì trên bài.
    /// </summary>
    private IQueryable<JoinedPost> Joined(Guid viewerId, bool ownOnly)
        => from post in _context.CommunityPosts.AsNoTracking()
           join author in _context.Users.AsNoTracking() on post.AuthorId equals author.Id
           join place in _context.CommunityPlaces.AsNoTracking() on post.PlaceId equals place.Id into places
           from place in places.DefaultIfEmpty()
           where ownOnly
               ? post.AuthorId == viewerId
               : post.AuthorId == viewerId
                   || (post.Status == CommunityContentStatus.Visible && !author.IsLocked && (post.PlaceId == null || place.HiddenAt == null))
           select new JoinedPost { Post = post, Place = place, DisplayName = author.DisplayName };

    private IQueryable<PostProjection> Rows(IQueryable<JoinedPost> query, Guid viewerId)
        => query.Select(j => new PostProjection
        {
            Post = j.Post,
            Place = j.Place,
            DisplayName = j.DisplayName,
            Votes = _context.CommunityPostVotes.Count(v => v.PostId == j.Post.Id),
            Comments = _context.CommunityComments.Count(c => c.PostId == j.Post.Id && c.Status == CommunityContentStatus.Visible),
            VotedByViewer = _context.CommunityPostVotes.Any(v => v.PostId == j.Post.Id && v.UserId == viewerId),
        });

    public async Task<CommunityPostRow?> GetPostRowAsync(Guid postId, Guid viewerId, CancellationToken ct = default)
        => (await Rows(Joined(viewerId, ownOnly: false).Where(j => j.Post.Id == postId), viewerId).FirstOrDefaultAsync(ct))?.ToRow();

    public async Task<List<CommunityPostRow>> ListPostsAsync(CommunityPostFilter filter, CancellationToken ct = default)
    {
        var query = Joined(filter.ViewerId, filter.Mine);
        if (!filter.Mine)
        {
            // Bảng tin không hiện bài chạm-một-lần, bài chỉ có ảnh chưa duyệt, và bài của chính mình đang bị ẩn.
            query = query.Where(j => j.Post.Status == CommunityContentStatus.Visible
                && (j.Post.Body != string.Empty || _context.CommunityPostImages.Any(i => i.PostId == j.Post.Id && i.Status == CommunityImageStatus.Approved)));
        }

        query = ApplyFilters(query, filter);
        var rows = filter.ByVotes
            ? Rows(query, filter.ViewerId).OrderByDescending(r => r.Votes).ThenByDescending(r => r.Post.CreatedAt).ThenByDescending(r => r.Post.Id).Skip(filter.Offset)
            : Rows(query, filter.ViewerId).OrderByDescending(r => r.Post.CreatedAt).ThenByDescending(r => r.Post.Id);
        var found = await rows.Take(filter.Limit).ToListAsync(ct);
        return found.Select(r => r.ToRow()).ToList();
    }

    private static IQueryable<JoinedPost> ApplyFilters(IQueryable<JoinedPost> query, CommunityPostFilter filter)
    {
        if (filter.Topic is { } topic)
        {
            query = query.Where(j => j.Post.Topic == topic);
        }

        if (filter.Tag is { } tag)
        {
            query = query.Where(j => j.Post.Tags.Contains(tag));
        }

        if (filter.PlaceId is { } placeId)
        {
            query = query.Where(j => j.Post.PlaceId == placeId);
        }

        // Khu vực chỉ lọc bài có quán. Bài không có quán không có khu vực để so, nên vẫn hiện ở mọi nơi.
        if (filter.ProvinceCode is { } province)
        {
            query = query.Where(j => j.Post.PlaceId == null || j.Place!.ProvinceCode == province);
        }

        if (filter.AreaCode is { } area)
        {
            query = query.Where(j => j.Post.PlaceId == null || j.Place!.AreaCode == area || j.Place!.AreaCode == string.Empty);
        }

        if (filter.Since is { } since)
        {
            query = query.Where(j => j.Post.CreatedAt >= since);
        }

        if (filter.BeforeCreatedAt is { } before && filter.BeforeId is { } beforeId)
        {
            query = query.Where(j => j.Post.CreatedAt < before || (j.Post.CreatedAt == before && j.Post.Id.CompareTo(beforeId) < 0));
        }

        return query;
    }

    public Task<List<CommunityImageInfo>> ListImagesAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct = default)
        => _context.CommunityPostImages.AsNoTracking()
            .Where(i => postIds.Contains(i.PostId) && i.Status != CommunityImageStatus.Deleted)
            .OrderBy(i => i.Position)
            .Select(i => new CommunityImageInfo(i.Id, i.PostId, i.Width, i.Height, i.Position, i.Status))
            .ToListAsync(ct);

    public Task<List<CommunityPlaceShareRow>> ListPlaceSharesAsync(string? provinceCode, string? areaCode, Guid? placeId, string? nameKeyContains, int limit, CancellationToken ct = default)
    {
        var query = from post in _context.CommunityPosts.AsNoTracking()
                    join author in _context.Users.AsNoTracking() on post.AuthorId equals author.Id
                    join place in _context.CommunityPlaces.AsNoTracking() on post.PlaceId equals place.Id
                    where post.Status == CommunityContentStatus.Visible && !author.IsLocked && place.HiddenAt == null
                    select new { post, place };

        if (provinceCode is not null)
        {
            query = query.Where(x => x.place.ProvinceCode == provinceCode);
        }

        if (areaCode is not null)
        {
            query = query.Where(x => x.place.AreaCode == areaCode || x.place.AreaCode == string.Empty);
        }

        if (placeId is not null)
        {
            query = query.Where(x => x.place.Id == placeId);
        }

        if (!string.IsNullOrEmpty(nameKeyContains))
        {
            query = query.Where(x => x.place.NameKey.Contains(nameKeyContains));
        }

        return query
            .OrderByDescending(x => x.post.CreatedAt)
            .Take(limit)
            .Select(x => new CommunityPlaceShareRow(
                x.place,
                x.post.PriceCents,
                x.post.SpentHereVerified,
                _context.CommunityPostVotes.Count(v => v.PostId == x.post.Id),
                x.post.CreatedAt,
                x.post.Tags))
            .ToListAsync(ct);
    }

    public Task<bool> HasVoteAsync(Guid postId, Guid userId, CancellationToken ct = default)
        => _context.CommunityPostVotes.AnyAsync(v => v.PostId == postId && v.UserId == userId, ct);

    public Task<int> CountVotesSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => _context.CommunityPostVotes.CountAsync(v => v.UserId == userId && v.CreatedAt >= since, ct);

    public Task AddVoteAsync(Guid postId, Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => _context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO community_post_votes (post_id, user_id, created_at) VALUES ({postId}, {userId}, {now}) ON CONFLICT (post_id, user_id) DO NOTHING", ct);

    public Task RemoveVoteAsync(Guid postId, Guid userId, CancellationToken ct = default)
        => _context.CommunityPostVotes.Where(v => v.PostId == postId && v.UserId == userId).ExecuteDeleteAsync(ct);

    public void AddComment(CommunityComment comment) => _context.CommunityComments.Add(comment);

    public async Task<List<CommunityCommentRow>> ListCommentsAsync(Guid postId, Guid viewerId, DateTimeOffset? afterCreatedAt, Guid? afterId, int limit, CancellationToken ct = default)
    {
        var query = from comment in _context.CommunityComments.AsNoTracking()
                    join author in _context.Users.AsNoTracking() on comment.AuthorId equals author.Id
                    where comment.PostId == postId
                        && (comment.AuthorId == viewerId || (comment.Status == CommunityContentStatus.Visible && !author.IsLocked))
                    select new { comment, author.DisplayName };

        if (afterCreatedAt is { } after && afterId is { } id)
        {
            query = query.Where(x => x.comment.CreatedAt > after || (x.comment.CreatedAt == after && x.comment.Id.CompareTo(id) > 0));
        }

        var rows = await query.OrderBy(x => x.comment.CreatedAt).ThenBy(x => x.comment.Id).Take(limit).ToListAsync(ct);
        return rows.Select(x => new CommunityCommentRow(x.comment, x.DisplayName)).ToList();
    }

    public Task<CommunityComment?> GetOwnCommentAsync(Guid commentId, Guid authorId, CancellationToken ct = default)
        => _context.CommunityComments.FirstOrDefaultAsync(c => c.Id == commentId && c.AuthorId == authorId, ct);

    public Task<int> CountCommentsSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default)
        => _context.CommunityComments.IgnoreQueryFilters().CountAsync(c => c.AuthorId == authorId && c.CreatedAt >= since, ct);

    public Task CloseOpenReportsOfCommentAsync(Guid commentId, DateTimeOffset now, CancellationToken ct = default)
        => _context.CommunityReports
            .Where(r => r.CommentId == commentId && r.Status == CommunityReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CommunityReportStatus.Closed).SetProperty(r => r.ResolvedAt, now), ct);

    public void AddImage(CommunityPostImage image) => _context.CommunityPostImages.Add(image);

    public Task<CommunityPostImage?> GetImageAsync(Guid imageId, CancellationToken ct = default)
        => _context.CommunityPostImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId, ct);

    public Task<CommunityPostImage?> GetImageOfPostAsync(Guid imageId, Guid postId, CancellationToken ct = default)
        => _context.CommunityPostImages.FirstOrDefaultAsync(i => i.Id == imageId && i.PostId == postId && i.Status != CommunityImageStatus.Deleted, ct);

    public Task<int> CountImagesSinceAsync(Guid authorId, DateTimeOffset since, CancellationToken ct = default)
        => (from image in _context.CommunityPostImages
            join post in _context.CommunityPosts.IgnoreQueryFilters() on image.PostId equals post.Id
            where post.AuthorId == authorId && image.CreatedAt >= since
            select image.Id).CountAsync(ct);

    public Task<int> CountPendingImagesAsync(CancellationToken ct = default)
        => _context.CommunityPostImages.CountAsync(i => i.Status == CommunityImageStatus.Pending, ct);

    public Task WipeImagesOfPostAsync(Guid postId, CancellationToken ct = default)
        => _context.CommunityPostImages
            .Where(i => i.PostId == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Data, Array.Empty<byte>()).SetProperty(i => i.SizeBytes, 0).SetProperty(i => i.Status, CommunityImageStatus.Deleted), ct);

    public Task CloseOpenReportsOfPostAsync(Guid postId, DateTimeOffset now, CancellationToken ct = default)
        => _context.CommunityReports
            .Where(r => r.PostId == postId && r.Status == CommunityReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CommunityReportStatus.Closed).SetProperty(r => r.ResolvedAt, now), ct);

    public void AddReport(CommunityReport report) => _context.CommunityReports.Add(report);

    public Task<CommunityComment?> GetVisibleCommentAsync(Guid commentId, Guid viewerId, CancellationToken ct = default)
        => (from comment in _context.CommunityComments.AsNoTracking()
            join post in Joined(viewerId, ownOnly: false) on comment.PostId equals post.Post.Id
            join author in _context.Users.AsNoTracking() on comment.AuthorId equals author.Id
            where comment.Id == commentId && comment.Status == CommunityContentStatus.Visible && !author.IsLocked
            select comment).FirstOrDefaultAsync(ct);

    public Task<bool> HasReportedPostAsync(Guid postId, Guid userId, CancellationToken ct = default)
        => _context.CommunityReports.AnyAsync(r => r.PostId == postId && r.ReporterId == userId, ct);

    public Task<bool> HasReportedCommentAsync(Guid commentId, Guid userId, CancellationToken ct = default)
        => _context.CommunityReports.AnyAsync(r => r.CommentId == commentId && r.ReporterId == userId, ct);

    public Task<int> CountReportsSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default)
        => _context.CommunityReports.CountAsync(r => r.ReporterId == userId && r.CreatedAt >= since, ct);

    public Task<int> CountOpenHidingReportsOfPostAsync(Guid postId, CancellationToken ct = default)
        => _context.CommunityReports.CountAsync(r => r.PostId == postId && r.Status == CommunityReportStatus.Open && r.Reason != CommunityReportReason.SelfHarm, ct);

    public Task<int> CountOpenHidingReportsOfCommentAsync(Guid commentId, CancellationToken ct = default)
        => _context.CommunityReports.CountAsync(r => r.CommentId == commentId && r.Status == CommunityReportStatus.Open && r.Reason != CommunityReportReason.SelfHarm, ct);

    public Task AutoHidePostAsync(Guid postId, CancellationToken ct = default)
        => _context.CommunityPosts.Where(p => p.Id == postId && p.Status == CommunityContentStatus.Visible)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CommunityContentStatus.AutoHidden), ct);

    public Task AutoHideCommentAsync(Guid commentId, CancellationToken ct = default)
        => _context.CommunityComments.Where(c => c.Id == commentId && c.Status == CommunityContentStatus.Visible)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CommunityContentStatus.AutoHidden), ct);

    public async Task<(List<OpsCommunityReportRow> Items, int Total)> ListOpenReportsAsync(int offset, int limit, CancellationToken ct = default)
    {
        var open = _context.CommunityReports.AsNoTracking().Where(r => r.Status == CommunityReportStatus.Open);
        var total = await open.CountAsync(ct);
        var reports = await open.OrderByDescending(r => r.Priority).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip(offset).Take(limit).ToListAsync(ct);

        var postIds = reports.Where(r => r.PostId is not null).Select(r => r.PostId!.Value).ToList();
        var commentIds = reports.Where(r => r.CommentId is not null).Select(r => r.CommentId!.Value).ToList();
        var posts = await (from p in _context.CommunityPosts.AsNoTracking()
                           join u in _context.Users.IgnoreQueryFilters().AsNoTracking() on p.AuthorId equals u.Id
                           where postIds.Contains(p.Id)
                           select new { p.Id, p.Title, p.Body, p.IsAnonymous, u.DisplayName, p.Status }).ToDictionaryAsync(x => x.Id, ct);
        var comments = await (from c in _context.CommunityComments.AsNoTracking()
                              join u in _context.Users.IgnoreQueryFilters().AsNoTracking() on c.AuthorId equals u.Id
                              where commentIds.Contains(c.Id)
                              select new { c.Id, c.Body, c.IsAnonymous, u.DisplayName, c.Status }).ToDictionaryAsync(x => x.Id, ct);

        var rows = new List<OpsCommunityReportRow>(reports.Count);
        foreach (var report in reports)
        {
            if (report.PostId is { } postId && posts.TryGetValue(postId, out var post))
            {
                rows.Add(new OpsCommunityReportRow(report, post.Title, post.Body, post.IsAnonymous, post.DisplayName, post.Status));
            }
            else if (report.CommentId is { } commentId && comments.TryGetValue(commentId, out var comment))
            {
                rows.Add(new OpsCommunityReportRow(report, null, comment.Body, comment.IsAnonymous, comment.DisplayName, comment.Status));
            }
        }

        return (rows, total);
    }

    public Task<CommunityReport?> GetReportAsync(Guid reportId, CancellationToken ct = default)
        => _context.CommunityReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);

    public async Task<bool> SetPostStatusAsync(Guid postId, CommunityContentStatus status, CancellationToken ct = default)
        => await _context.CommunityPosts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status), ct) > 0;

    public async Task<bool> SetCommentStatusAsync(Guid commentId, CommunityContentStatus status, CancellationToken ct = default)
        => await _context.CommunityComments.Where(c => c.Id == commentId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status), ct) > 0;

    public Task ResolveReportsOfPostAsync(Guid postId, CommunityReportStatus status, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
        => _context.CommunityReports.Where(r => r.PostId == postId && r.Status == CommunityReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.ResolvedBy, actorId).SetProperty(r => r.ResolvedAt, now), ct);

    public Task ResolveReportsOfCommentAsync(Guid commentId, CommunityReportStatus status, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
        => _context.CommunityReports.Where(r => r.CommentId == commentId && r.Status == CommunityReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.ResolvedBy, actorId).SetProperty(r => r.ResolvedAt, now), ct);

    public async Task<(List<OpsCommunityImageRow> Items, int Total)> ListPendingImagesAsync(int offset, int limit, CancellationToken ct = default)
    {
        var query = from image in _context.CommunityPostImages.AsNoTracking()
                    join post in _context.CommunityPosts.AsNoTracking() on image.PostId equals post.Id
                    where image.Status == CommunityImageStatus.Pending
                    orderby image.CreatedAt, image.Id
                    select new OpsCommunityImageRow(image.Id, image.PostId, image.Width, image.Height, post.Title, post.Body, image.CreatedAt);
        return (await query.Skip(offset).Take(limit).ToListAsync(ct), await query.CountAsync(ct));
    }

    public async Task<bool> ReviewImageAsync(Guid imageId, bool approve, Guid actorId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (approve)
        {
            return await _context.CommunityPostImages.Where(i => i.Id == imageId && i.Status == CommunityImageStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, CommunityImageStatus.Approved).SetProperty(i => i.ReviewedBy, actorId).SetProperty(i => i.ReviewedAt, now), ct) > 0;
        }

        return await _context.CommunityPostImages.Where(i => i.Id == imageId && (i.Status == CommunityImageStatus.Pending || i.Status == CommunityImageStatus.Approved))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, CommunityImageStatus.Rejected).SetProperty(i => i.Data, Array.Empty<byte>()).SetProperty(i => i.SizeBytes, 0).SetProperty(i => i.ReviewedBy, actorId).SetProperty(i => i.ReviewedAt, now), ct) > 0;
    }

    public async Task<bool> HidePlaceAsync(Guid placeId, DateTimeOffset now, CancellationToken ct = default)
        => await _context.CommunityPlaces.Where(p => p.Id == placeId).ExecuteUpdateAsync(s => s.SetProperty(p => p.HiddenAt, now).SetProperty(p => p.UpdatedAt, now), ct) > 0;

    public async Task<bool> MuteAuthorOfPostAsync(Guid postId, DateTimeOffset until, CancellationToken ct = default)
    {
        var authorId = await _context.CommunityPosts.Where(p => p.Id == postId).Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync(ct);
        return authorId is { } id && await MuteAsync(id, until, ct);
    }

    public async Task<bool> MuteAuthorOfCommentAsync(Guid commentId, DateTimeOffset until, CancellationToken ct = default)
    {
        var authorId = await _context.CommunityComments.Where(c => c.Id == commentId).Select(c => (Guid?)c.AuthorId).FirstOrDefaultAsync(ct);
        return authorId is { } id && await MuteAsync(id, until, ct);
    }

    private async Task<bool> MuteAsync(Guid userId, DateTimeOffset until, CancellationToken ct)
        => await _context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.CommunityMutedUntil, until), ct) > 0;

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
