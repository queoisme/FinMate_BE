using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Application.Community;

public interface ICommunityReadService
{
    Task<IReadOnlyList<CommunityTopic>> GetTopicsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CommunityProvince>> GetRegionsAsync(CancellationToken ct = default);
    Task<CommunityPostPageDto> ListPostsAsync(Guid viewerId, CommunityPostQuery query, CancellationToken ct = default);
    Task<CommunityPostDto> GetAsync(Guid viewerId, Guid postId, CancellationToken ct = default);

    /// <summary>Bài của chính người gọi, dùng sau khi đăng hoặc sửa.</summary>
    Task<CommunityPostDto> GetOwnAsync(Guid userId, Guid postId, CancellationToken ct = default);
    Task<CommunityPlacePageDto> ListPlacesAsync(Guid viewerId, CommunityPlaceQuery query, CancellationToken ct = default);
    Task<CommunityPlaceDetailDto> GetPlaceAsync(Guid viewerId, Guid placeId, CancellationToken ct = default);
}

public sealed class CommunityReadService : ICommunityReadService
{
    private const string SortTop = "top";
    private const string SortNew = "new";
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    private const int TopWindowDays = 30;
    private const int MaxPlaceShares = 2000;
    private const int PlacePostLimit = 20;

    private readonly ICommunityRepository _community;
    private readonly ISystemConfigProvider _config;

    public CommunityReadService(ICommunityRepository community, ISystemConfigProvider config)
    {
        _community = community;
        _config = config;
    }

    private async Task<CommunitySettings> SettingsAsync(CancellationToken ct) => (await _config.GetAsync(ct)).Document.Community;

    public async Task<IReadOnlyList<CommunityTopic>> GetTopicsAsync(CancellationToken ct = default) => (await SettingsAsync(ct)).Topics;

    public async Task<IReadOnlyList<CommunityProvince>> GetRegionsAsync(CancellationToken ct = default) => (await SettingsAsync(ct)).Regions;

    public async Task<CommunityPostPageDto> ListPostsAsync(Guid viewerId, CommunityPostQuery query, CancellationToken ct = default)
    {
        var sort = query.Sort ?? SortNew;
        if (sort is not (SortNew or SortTop))
        {
            throw new BusinessRuleException(CommunityErrorCodes.InvalidInput, "Cách sắp xếp không hợp lệ.");
        }

        var byVotes = sort == SortTop;
        var positionId = byVotes ? null : CommunityCursor.ReadPosition(query.Cursor);
        var positionTime = positionId is { } id
            ? await _community.GetPostCreatedAtAsync(id, ct) ?? throw CommunityCursor.Invalid()
            : (DateTimeOffset?)null;
        var offset = byVotes ? CommunityCursor.ReadOffset(query.Cursor) : 0;
        var limit = ClampLimit(query.Limit);

        var rows = await _community.ListPostsAsync(
            new CommunityPostFilter(
                viewerId, query.Topic, CommunitySettings.PlacesTopic, query.ProvinceCode, query.AreaCode, query.Tag, null, query.Mine, byVotes,
                byVotes ? DateTimeOffset.UtcNow.AddDays(-TopWindowDays) : null, positionTime, positionId, offset, limit + 1),
            ct);

        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        var next = !hasMore ? null
            : byVotes ? CommunityCursor.FromOffset(offset + limit)
            : CommunityCursor.FromPosition(page[^1].Post.Id);
        return new CommunityPostPageDto(await ToDtosAsync(viewerId, page, ct), next);
    }

    public async Task<CommunityPostDto> GetAsync(Guid viewerId, Guid postId, CancellationToken ct = default)
    {
        var row = await _community.GetPostRowAsync(postId, viewerId, ct) ?? throw new NotFoundException("CommunityPost", postId);
        return (await ToDtosAsync(viewerId, [row], ct))[0];
    }

    public Task<CommunityPostDto> GetOwnAsync(Guid userId, Guid postId, CancellationToken ct = default) => GetAsync(userId, postId, ct);

    public async Task<CommunityPlacePageDto> ListPlacesAsync(Guid viewerId, CommunityPlaceQuery query, CancellationToken ct = default)
    {
        var offset = CommunityCursor.ReadOffset(query.Cursor);
        var limit = ClampLimit(query.Limit);
        var key = CounterpartyKey.From(query.Q);
        var shares = await _community.ListPlaceSharesAsync(query.ProvinceCode, query.AreaCode, null, key, MaxPlaceShares, ct);

        var now = DateTimeOffset.UtcNow;
        var ranked = shares
            .GroupBy(s => s.Place.Id)
            .Select(group => (Place: group.First().Place, Shares: ToShares(group)))
            .Select(x => (x.Shares, Summary: CommunityPlaceRanker.Summarize(x.Place, x.Shares), Score: CommunityPlaceRanker.Score(x.Shares, now)))
            .Where(x => query.Tag is null || x.Summary.Tags.Contains(query.Tag))
            .Where(x => query.MaxPriceCents is null || x.Summary.TypicalPriceCents <= query.MaxPriceCents)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Summary.Id)
            .Select(x => x.Summary)
            .ToList();

        var page = ranked.Skip(offset).Take(limit).ToList();
        var next = offset + limit < ranked.Count ? CommunityCursor.FromOffset(offset + limit) : null;
        return new CommunityPlacePageDto(page, next);
    }

    public async Task<CommunityPlaceDetailDto> GetPlaceAsync(Guid viewerId, Guid placeId, CancellationToken ct = default)
    {
        var shares = await _community.ListPlaceSharesAsync(null, null, placeId, null, MaxPlaceShares, ct);
        if (shares.Count == 0)
        {
            throw new NotFoundException("CommunityPlace", placeId);
        }

        var summary = CommunityPlaceRanker.Summarize(shares[0].Place, ToShares(shares));
        var rows = await _community.ListPostsAsync(
            new CommunityPostFilter(viewerId, null, null, null, null, null, placeId, false, false, null, null, null, 0, PlacePostLimit), ct);
        return new CommunityPlaceDetailDto(summary, await ToDtosAsync(viewerId, rows, ct));
    }

    private static List<CommunityPlaceShare> ToShares(IEnumerable<CommunityPlaceShareRow> rows)
        => rows.Select(s => new CommunityPlaceShare(s.PriceCents, s.SpentHereVerified, s.Votes, s.CreatedAt, s.Tags)).ToList();

    private static int ClampLimit(int limit) => limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

    private async Task<IReadOnlyList<CommunityPostDto>> ToDtosAsync(Guid viewerId, IReadOnlyList<CommunityPostRow> rows, CancellationToken ct)
    {
        var images = (await _community.ListImagesAsync(rows.Select(r => r.Post.Id).ToList(), ct)).ToLookup(i => i.PostId);
        return rows.Select(row => ToDto(viewerId, row, images[row.Post.Id])).ToList();
    }

    private static CommunityPostDto ToDto(Guid viewerId, CommunityPostRow row, IEnumerable<CommunityImageInfo> images)
    {
        var post = row.Post;
        var isMine = post.AuthorId == viewerId;
        var place = row.Place is { } p ? new CommunityPlaceDto(p.Id, p.Name, p.ProvinceCode, p.AreaCode, p.AddressText) : null;
        return new CommunityPostDto(
            post.Id,
            post.Topic,
            post.Title,
            post.Body,
            post.Tags,
            CommunityAuthorView.From(post.IsAnonymous, row.AuthorDisplayName),
            isMine,
            VietnamTime.DateOf(post.CreatedAt),
            post.EditedAt is not null,
            row.Votes,
            row.Comments,
            row.VotedByViewer,
            post.SpentHereVerified,
            post.PriceCents,
            place,
            images.Where(i => isMine || i.Status == CommunityImageStatus.Approved)
                .Select(i => new CommunityImageDto(i.Id, i.Width, i.Height, i.Status.ToString().ToLowerInvariant()))
                .ToList(),
            isMine ? CommunityRules.Snake(post.Status) : null);
    }
}
