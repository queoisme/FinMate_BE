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
            .Where(i => postIds.Contains(i.PostId))
            .OrderBy(i => i.Position)
            .Select(i => new CommunityImageInfo(i.Id, i.PostId, i.Width, i.Height, i.Status))
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

    public Task RemoveImagesOfPostAsync(Guid postId, CancellationToken ct = default)
        => _context.CommunityPostImages.Where(i => i.PostId == postId).ExecuteDeleteAsync(ct);

    public Task CloseOpenReportsOfPostAsync(Guid postId, DateTimeOffset now, CancellationToken ct = default)
        => _context.CommunityReports
            .Where(r => r.PostId == postId && r.Status == CommunityReportStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, CommunityReportStatus.Closed).SetProperty(r => r.ResolvedAt, now), ct);

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
