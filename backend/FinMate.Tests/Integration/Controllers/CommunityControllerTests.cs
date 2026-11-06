using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Ẩn danh là mặc định và không có đường lộ tác giả; bài ngoài chủ đề quán không có khu vực; cờ tắt thì API tắt.</summary>
[Collection("Integration")]
public class CommunityControllerTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Base = "/api/v1/community";
    private const string Hcm = "ho-chi-minh";
    private const string District7 = "quan-7";
    private const string Hanoi = "ha-noi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static bool _enabled;

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;
    private string _admin = string.Empty;

    public CommunityControllerTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record Account(Guid Id, string Email, string Token);

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);
        _admin = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);
        if (!_enabled)
        {
            await SetEnabledAsync(true);
            _enabled = true;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SetEnabledAsync(bool enabled)
    {
        const string config = "/api/v1/admin/system-config";
        var version = (await DataAsync(await SendAsync(HttpMethod.Get, config, _admin))).GetProperty("version").GetInt32();
        var saved = await SendAsync(HttpMethod.Put, config, _admin, new { expectedVersion = version, content = new { features = new { community = enabled } }, note = "kiem thu" });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private async Task<Account> AccountAsync(string displayName = "Nguyễn Văn Tên Thật", bool consent = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@finmate.local",
            DisplayName = displayName,
            Role = UserRole.User,
            CommunityConsentVersion = consent ? ConsentVersions.Community : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Id, user.Email, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotBeNullOrEmpty($"status {response.StatusCode}");
        return JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    // Chỉ dùng chữ cái: Guid có thể cho ra chuỗi số dài, mà bộ lọc nội dung chặn đúng loại đó.
    private static string UniqueWord() => string.Concat(Guid.NewGuid().ToString("N").Take(12).Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))));

    private static string UniqueName() => $"Quán {UniqueWord()}";

    private async Task<JsonElement> ShareExperienceAsync(Account author, string body = "Mình nấu cơm ở nhà và giảm được một nửa tiền ăn", bool? isAnonymous = null)
        => await DataAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body, isAnonymous }));

    private async Task<JsonElement> SharePlaceAsync(Account author, string name, string province = Hcm, string? area = District7, string? body = "Cơm ngon, giá ổn", long? priceCents = 30_000, Guid? transactionId = null)
        => await DataAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "places", placeName = name, provinceCode = province, areaCode = area, body, priceCents, transactionId, tags = new[] { "gia_tot" } }));

    private async Task<Guid> SeedDebitAsync(Account owner, string merchant)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = owner.Id, AccountName = "Ví", AccountType = AccountType.Cash, CreatedAt = now, UpdatedAt = now };
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            FinancialAccountId = account.Id,
            AmountCents = 30_000,
            TransactionType = TransactionType.Debit,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            MerchantName = merchant,
            TransactedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.FinancialAccounts.Add(account);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    [Fact]
    public async Task EveryEndpointIsOffWhileTheFlagIsOff()
    {
        var user = await AccountAsync();
        await SetEnabledAsync(false);
        try
        {
            var response = await SendAsync(HttpMethod.Get, $"{Base}/posts", user.Token);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_DISABLED");
        }
        finally
        {
            await SetEnabledAsync(true);
        }
    }

    [Fact]
    public async Task PublicAppConfigDoesNotRevealTheBannedWords()
    {
        var text = await (await _client.GetAsync("/api/v1/app-config")).Content.ReadAsStringAsync();
        text.Should().NotContain("cam kết lợi nhuận");
    }

    [Fact]
    public async Task PostingNeedsConsent_ReadingDoesNot()
    {
        var user = await AccountAsync(consent: false);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts", user.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var post = await SendAsync(HttpMethod.Post, $"{Base}/posts", user.Token, new { topic = "experience", body = "Một chia sẻ" });
        post.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(post)).Should().Be("COMMUNITY_CONSENT_REQUIRED");

        (await SendAsync(HttpMethod.Post, "/api/v1/users/me/consents", user.Token, new { kind = "community", version = ConsentVersions.Community })).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Post, $"{Base}/posts", user.Token, new { topic = "experience", body = "Một chia sẻ" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task APostIsAnonymousUnlessTheAuthorSaysOtherwise_AndNeverCarriesTheirIdentity()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var id = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", reader.Token);
        var raw = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(raw).RootElement.GetProperty("data");
        data.GetProperty("author").GetProperty("isAnonymous").GetBoolean().Should().BeTrue();
        data.GetProperty("author").GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("isMine").GetBoolean().Should().BeFalse();
        raw.Should().NotContain(author.Id.ToString()).And.NotContain(author.Email).And.NotContain("Tên Thật");
    }

    [Fact]
    public async Task TurningAnonymityOffShowsOnlyTheDisplayName()
    {
        var author = await AccountAsync("Chị Lan");
        var reader = await AccountAsync();
        var id = (await ShareExperienceAsync(author, isAnonymous: false)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", reader.Token);
        var raw = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(raw).RootElement.GetProperty("data").GetProperty("author").GetProperty("displayName").GetString().Should().Be("Chị Lan");
        raw.Should().NotContain(author.Id.ToString()).And.NotContain(author.Email);

        // Đổi lại sau khi đăng.
        (await SendAsync(new HttpMethod("PATCH"), $"{Base}/posts/{id}", author.Token, new { isAnonymous = true })).IsSuccessStatusCode.Should().BeTrue();
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", reader.Token))).GetProperty("author").GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ADisplayNameHoldingAPhoneNumberCannotGoPublic()
    {
        var author = await AccountAsync("Lan 0912 345 678");
        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body = "Một chia sẻ", isAnonymous = false });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_DISPLAY_NAME_NOT_ALLOWED");
    }

    [Theory]
    [InlineData("Xem thêm ở https://lua.example")]
    [InlineData("Vào shop.vn nhé")]
    [InlineData("Gọi mình 0912 345 678")]
    [InlineData("Gọi +84.912.345.678")]
    [InlineData("Email lan@example.com")]
    [InlineData("STK của mình 123456789012")]
    [InlineData("Cam kết lợi nhuận mỗi tháng")]
    public async Task ContentThatLeaksContactsOrLooksLikeAScamIsRefused(string body)
    {
        var author = await AccountAsync();
        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_CONTENT_NOT_ALLOWED");
    }

    [Theory]
    [InlineData("Mình đang nợ 2.000.000.000 đồng, các bạn có lời khuyên không")]
    [InlineData("Mình phải vay tiền để trả nợ, rất áp lực")]
    public async Task TalkingAboutDebtIsNotBlocked(string body)
    {
        var author = await AccountAsync();
        (await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "difficulty", body })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ADifficultyPostCarriesNoPlaceOrFinancialLink()
    {
        var author = await AccountAsync();
        var transactionId = await SeedDebitAsync(author, "Quán X");

        foreach (var payload in new object[]
        {
            new { topic = "difficulty", body = "Khó khăn", placeName = "Quán X", provinceCode = Hcm },
            new { topic = "difficulty", body = "Khó khăn", priceCents = 30_000L },
            new { topic = "difficulty", body = "Khó khăn", transactionId },
        })
        {
            var refused = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, payload);
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ErrorCodeAsync(refused)).Should().Be("COMMUNITY_PLACE_NOT_ALLOWED");
        }

        var data = await DataAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "difficulty", body = "Khó khăn thật" }));
        data.GetProperty("place").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TopicRulesAreEnforced()
    {
        var author = await AccountAsync();

        var noPlace = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "places", body = "Ngon" });
        (await ErrorCodeAsync(noPlace)).Should().Be("COMMUNITY_PLACE_REQUIRED");

        var unknown = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "khong-co", body = "Ngon" });
        (await ErrorCodeAsync(unknown)).Should().Be("COMMUNITY_TOPIC_UNKNOWN");

        var noBody = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "question", body = "  " });
        (await ErrorCodeAsync(noBody)).Should().Be("COMMUNITY_INVALID_INPUT");

        var badTag = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "question", body = "Hỏi", tags = new[] { "khong-co" } });
        (await ErrorCodeAsync(badTag)).Should().Be("COMMUNITY_INVALID_INPUT");

        var badRegion = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "places", placeName = UniqueName(), provinceCode = "khong-co" });
        (await ErrorCodeAsync(badRegion)).Should().Be("COMMUNITY_REGION_UNKNOWN");
    }

    [Fact]
    public async Task SharingTheSamePlaceAgainUpdatesTheOldPost()
    {
        var author = await AccountAsync();
        var name = UniqueName();
        var first = await SharePlaceAsync(author, name);
        var second = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "places", placeName = name.ToUpperInvariant(), provinceCode = Hcm, areaCode = District7, body = "Vẫn ngon", priceCents = 35_000L });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync(second);
        data.GetProperty("id").GetGuid().Should().Be(first.GetProperty("id").GetGuid());
        data.GetProperty("body").GetString().Should().Be("Vẫn ngon");
        data.GetProperty("priceCents").GetInt64().Should().Be(35_000);
    }

    [Fact]
    public async Task ReadingByRegionLimitsPlacesButNotOtherTopics()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var place = await SharePlaceAsync(author, UniqueName());
        var story = await ShareExperienceAsync(author);

        var ids = async (string url) =>
        {
            var data = await DataAsync(await SendAsync(HttpMethod.Get, url, reader.Token));
            return data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        };

        var inHanoi = await ids($"{Base}/posts?provinceCode={Hanoi}&limit=50");
        inHanoi.Should().NotContain(place.GetProperty("id").GetGuid());
        inHanoi.Should().Contain(story.GetProperty("id").GetGuid());

        var inHcm = await ids($"{Base}/posts?provinceCode={Hcm}&areaCode={District7}&limit=50");
        inHcm.Should().Contain(place.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AOneTapShareCountsForThePlaceButStaysOutOfTheFeed()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var name = UniqueName();
        var post = await SharePlaceAsync(author, name, body: null);

        var feed = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts?topic=places&limit=50", reader.Token));
        feed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(post.GetProperty("id").GetGuid());

        var places = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/places?provinceCode={Hcm}&areaCode={District7}&q={Uri.EscapeDataString(name)}", reader.Token));
        var found = places.GetProperty("items")[0];
        found.GetProperty("name").GetString().Should().Be(name);
        found.GetProperty("shareCount").GetInt32().Should().Be(1);
        found.GetProperty("typicalPriceCents").GetInt64().Should().Be(30_000);
    }

    [Fact]
    public async Task PlacesAreRankedByRealSpendingAndFilteredByPrice()
    {
        var verifiedAuthor = await AccountAsync();
        var plainAuthor = await AccountAsync();
        var voter1 = await AccountAsync();
        var voter2 = await AccountAsync();
        var prefix = $"Hạng {UniqueWord()}";
        var verifiedName = prefix + " A";
        var plainName = prefix + " B";

        var transactionId = await SeedDebitAsync(verifiedAuthor, verifiedName);
        await SharePlaceAsync(verifiedAuthor, verifiedName, priceCents: 40_000, transactionId: transactionId);
        var plain = await SharePlaceAsync(plainAuthor, plainName, priceCents: 90_000);
        foreach (var voter in new[] { voter1, voter2 })
        {
            (await SendAsync(HttpMethod.Put, $"{Base}/posts/{plain.GetProperty("id").GetGuid()}/vote", voter.Token)).IsSuccessStatusCode.Should().BeTrue();
        }

        var url = $"{Base}/places?provinceCode={Hcm}&areaCode={District7}&q={Uri.EscapeDataString(prefix)}";
        var ranked = await DataAsync(await SendAsync(HttpMethod.Get, url, voter1.Token));
        ranked.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Should().ContainInOrder(verifiedName, plainName);

        var cheap = await DataAsync(await SendAsync(HttpMethod.Get, url + "&maxPriceCents=50000", voter1.Token));
        cheap.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Should().Equal(verifiedName);
    }

    [Fact]
    public async Task OnlyARealSpendOfYourOwnEarnsTheBadge()
    {
        var author = await AccountAsync();
        var stranger = await AccountAsync();
        var name = UniqueName();

        var own = await SeedDebitAsync(author, name);
        (await SharePlaceAsync(author, name, transactionId: own)).GetProperty("spentHereVerified").GetBoolean().Should().BeTrue();

        var theirs = await SeedDebitAsync(author, name + "x");
        (await SharePlaceAsync(stranger, name + "x", transactionId: theirs)).GetProperty("spentHereVerified").GetBoolean().Should().BeFalse();

        var otherShop = await SeedDebitAsync(stranger, "Một quán hoàn toàn khác");
        (await SharePlaceAsync(stranger, name + "y", transactionId: otherShop)).GetProperty("spentHereVerified").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task VotingTwiceCountsOnce_AndYouCannotVoteForYourself()
    {
        var author = await AccountAsync();
        var voter = await AccountAsync();
        var id = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Put, $"{Base}/posts/{id}/vote", voter.Token)).IsSuccessStatusCode.Should().BeTrue();
        var again = await DataAsync(await SendAsync(HttpMethod.Put, $"{Base}/posts/{id}/vote", voter.Token));
        again.GetProperty("voteCount").GetInt32().Should().Be(1);
        again.GetProperty("votedByMe").GetBoolean().Should().BeTrue();

        var own = await SendAsync(HttpMethod.Put, $"{Base}/posts/{id}/vote", author.Token);
        (await ErrorCodeAsync(own)).Should().Be("COMMUNITY_OWN_POST");

        var removed = await DataAsync(await SendAsync(HttpMethod.Delete, $"{Base}/posts/{id}/vote", voter.Token));
        removed.GetProperty("voteCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task OnlyTheAuthorCanEditOrDelete()
    {
        var author = await AccountAsync();
        var stranger = await AccountAsync();
        var id = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await SendAsync(new HttpMethod("PATCH"), $"{Base}/posts/{id}", stranger.Token, new { body = "Sửa trộm" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{id}", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{id}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var row = await db.CommunityPosts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id);
        row.Body.Should().BeEmpty();
        row.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ThePostingLimitCountsDeletedPostsToo()
    {
        var author = await AccountAsync();
        for (var i = 0; i < 5; i++)
        {
            var id = (await ShareExperienceAsync(author, $"Chia sẻ lần {i}")).GetProperty("id").GetGuid();
            await SendAsync(HttpMethod.Delete, $"{Base}/posts/{id}", author.Token);
        }

        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body = "Lần thứ sáu" });
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_LIMIT_REACHED");
    }

    [Fact]
    public async Task AMutedAuthorCanReadButNotPost()
    {
        var author = await AccountAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Users.Where(u => u.Id == author.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CommunityMutedUntil, DateTimeOffset.UtcNow.AddDays(1)));
        }

        (await SendAsync(HttpMethod.Get, $"{Base}/posts", author.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body = "Một chia sẻ" });
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_MUTED");
    }

    [Fact]
    public async Task PostsOfALockedOrDeletingAccountDisappearAndCanComeBack()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var id = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        async Task SetLockedAsync(bool locked)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.Users.Where(u => u.Id == author.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsLocked, locked));
        }

        await SetLockedAsync(true);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await SetLockedAsync(false);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{id}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HardDeletingAUserRemovesTheirContentAndEmptiesThePlace()
    {
        var author = await AccountAsync();
        var voter = await AccountAsync();
        var reader = await AccountAsync();
        var name = UniqueName();
        var post = await SharePlaceAsync(author, name);
        var other = await ShareExperienceAsync(reader);
        (await SendAsync(HttpMethod.Put, $"{Base}/posts/{other.GetProperty("id").GetGuid()}/vote", author.Token)).IsSuccessStatusCode.Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(author.Id);
        }

        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{post.GetProperty("id").GetGuid()}", voter.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var places = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/places?provinceCode={Hcm}&areaCode={District7}&q={Uri.EscapeDataString(name)}", voter.Token));
        places.GetProperty("items").GetArrayLength().Should().Be(0);
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{other.GetProperty("id").GetGuid()}", voter.Token))).GetProperty("voteCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RegionsAndProfileCanBeReadAndSaved()
    {
        var user = await AccountAsync();
        var regions = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/regions", user.Token));
        regions.GetArrayLength().Should().Be(34);
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/topics", user.Token))).GetArrayLength().Should().Be(4);

        var saved = await DataAsync(await SendAsync(new HttpMethod("PATCH"), $"{Base}/me", user.Token, new { provinceCode = Hcm, areaCode = District7 }));
        saved.GetProperty("provinceCode").GetString().Should().Be(Hcm);
        saved.GetProperty("promptsEnabled").GetBoolean().Should().BeTrue();

        var wrongArea = await SendAsync(new HttpMethod("PATCH"), $"{Base}/me", user.Token, new { provinceCode = Hanoi, areaCode = District7 });
        (await ErrorCodeAsync(wrongArea)).Should().Be("COMMUNITY_REGION_UNKNOWN");
    }

    [Fact]
    public async Task MyPostsIncludeAOneTapShareSoItCanBeDeleted()
    {
        var author = await AccountAsync();
        var post = await SharePlaceAsync(author, UniqueName(), body: null);
        var mine = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts?mine=true", author.Token));
        mine.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(post.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task FeedPagesDoNotRepeat()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        for (var i = 0; i < 3; i++)
        {
            await ShareExperienceAsync(author, $"Bài phân trang {i}");
        }

        var first = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts?topic=experience&limit=2", reader.Token));
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();
        var second = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts?topic=experience&limit=2&cursor={Uri.EscapeDataString(cursor!)}", reader.Token));
        var firstIds = first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());
        second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotIntersectWith(firstIds);

        var bad = await SendAsync(HttpMethod.Get, $"{Base}/posts?cursor=khong-hop-le", reader.Token);
        (await ErrorCodeAsync(bad)).Should().Be("COMMUNITY_INVALID_INPUT");
    }
}
