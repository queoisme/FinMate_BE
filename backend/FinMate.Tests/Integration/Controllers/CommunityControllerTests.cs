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

    private async Task<Account> AccountAsync(string displayName = "Nguyễn Văn Tên Thật", bool consent = true, UserRole role = UserRole.User)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@finmate.local",
            DisplayName = displayName,
            Role = role,
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

    private async Task<Guid> SeedDebitAsync(Account owner, string? merchant, string? categoryName = null, long amountCents = 30_000, TransactionType type = TransactionType.Debit, TransactionStatus status = TransactionStatus.Confirmed)
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
            AmountCents = amountCents,
            TransactionType = type,
            Source = TransactionSource.Manual,
            Status = status,
            MerchantName = merchant,
            TransactedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (categoryName is not null)
        {
            var category = new Category { Id = Guid.NewGuid(), UserId = owner.Id, Name = categoryName, Slug = $"tu-dat-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now };
            db.Categories.Add(category);
            transaction.CategoryId = category.Id;
        }

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

    private async Task<JsonElement> CommentAsync(Account author, Guid postId, string body = "Cảm ơn bạn đã chia sẻ", bool? isAnonymous = null)
        => await DataAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", author.Token, new { body, isAnonymous }));

    [Fact]
    public async Task ACommentIsAnonymousByDefault_AndNeverCarriesTheAuthor()
    {
        var author = await AccountAsync();
        var commenter = await AccountAsync("Anh Minh");
        var reader = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await CommentAsync(commenter, postId)).GetProperty("author").GetProperty("isAnonymous").GetBoolean().Should().BeTrue();
        (await CommentAsync(commenter, postId, "Bình luận có tên", isAnonymous: false)).GetProperty("author").GetProperty("displayName").GetString().Should().Be("Anh Minh");

        var response = await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments", reader.Token);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(commenter.Id.ToString()).And.NotContain(commenter.Email);
        var items = JsonDocument.Parse(raw).RootElement.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("author").GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
        items[0].GetProperty("isMine").GetBoolean().Should().BeFalse();

        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", reader.Token))).GetProperty("commentCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ACommentByThePostAuthorHasNoMarkLinkingItToTheirAnonymousPost()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        await CommentAsync(author, postId, "Mình bổ sung thêm", isAnonymous: false);

        var strangerComment = await AccountAsync("Người lạ");
        await CommentAsync(strangerComment, postId, "Mình cũng vậy", isAnonymous: false);

        var items = (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments", reader.Token))).GetProperty("items");
        var fieldsOfAuthor = items[0].EnumerateObject().Select(p => p.Name).ToList();
        var fieldsOfStranger = items[1].EnumerateObject().Select(p => p.Name).ToList();
        fieldsOfAuthor.Should().Equal(fieldsOfStranger);
        fieldsOfAuthor.Should().NotContain(name => name.Contains("isPostAuthor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CommentsFollowTheContentRules()
    {
        var author = await AccountAsync();
        var noConsent = await AccountAsync(consent: false);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        var phone = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", author.Token, new { body = "Gọi 0912345678" });
        (await ErrorCodeAsync(phone)).Should().Be("COMMUNITY_CONTENT_NOT_ALLOWED");

        var empty = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", author.Token, new { body = "  " });
        (await ErrorCodeAsync(empty)).Should().Be("COMMUNITY_INVALID_INPUT");

        var tooLong = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", author.Token, new { body = new string('a', 301) });
        (await ErrorCodeAsync(tooLong)).Should().Be("COMMUNITY_INVALID_INPUT");

        var consent = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", noConsent.Token, new { body = "Chào" });
        (await ErrorCodeAsync(consent)).Should().Be("COMMUNITY_CONSENT_REQUIRED");
    }

    [Fact]
    public async Task OneTapSharesCannotBeCommentedOn()
    {
        var author = await AccountAsync();
        var commenter = await AccountAsync();
        var postId = (await SharePlaceAsync(author, UniqueName(), body: null)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", commenter.Token, new { body = "Chào" });
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_COMMENTS_NOT_ALLOWED");
    }

    [Fact]
    public async Task OnlyTheCommenterCanEditOrDeleteTheirComment()
    {
        var author = await AccountAsync();
        var commenter = await AccountAsync("Anh Minh");
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var id = (await CommentAsync(commenter, postId)).GetProperty("id").GetGuid();

        (await SendAsync(new HttpMethod("PATCH"), $"{Base}/comments/{id}", author.Token, new { body = "Sửa trộm" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"{Base}/comments/{id}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var edited = await DataAsync(await SendAsync(new HttpMethod("PATCH"), $"{Base}/comments/{id}", commenter.Token, new { body = "Đã sửa", isAnonymous = false }));
        edited.GetProperty("body").GetString().Should().Be("Đã sửa");
        edited.GetProperty("edited").GetBoolean().Should().BeTrue();
        edited.GetProperty("author").GetProperty("displayName").GetString().Should().Be("Anh Minh");

        (await SendAsync(HttpMethod.Delete, $"{Base}/comments/{id}", commenter.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments", author.Token))).GetProperty("items").GetArrayLength().Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.CommunityComments.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id)).Body.Should().BeEmpty();
    }

    [Fact]
    public async Task CommentsOnADeletedPostAreGone()
    {
        var author = await AccountAsync();
        var commenter = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        await CommentAsync(commenter, postId);

        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments", commenter.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", commenter.Token, new { body = "Chào" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CommentsPageWithoutRepeating()
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        for (var i = 0; i < 3; i++)
        {
            await CommentAsync(author, postId, $"Bình luận {i}");
        }

        var first = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments?limit=2", author.Token));
        first.GetProperty("items").GetArrayLength().Should().Be(2);
        var cursor = first.GetProperty("nextCursor").GetString()!;
        var second = await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments?limit=2&cursor={Uri.EscapeDataString(cursor)}", author.Token));
        second.GetProperty("items").GetArrayLength().Should().Be(1);
        second.GetProperty("items")[0].GetProperty("body").GetString().Should().Be("Bình luận 2");
    }

    [Fact]
    public async Task TheCommentLimitCountsDeletedCommentsToo()
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.CommunityComments.AddRange(Enumerable.Range(0, 30).Select(i => new CommunityComment { Id = Guid.NewGuid(), PostId = postId, AuthorId = author.Id, Body = string.Empty, CreatedAt = now, UpdatedAt = now, DeletedAt = now }));
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/comments", author.Token, new { body = "Thêm một cái" });
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_LIMIT_REACHED");
    }

    private Task<HttpResponseMessage> UploadAsync(Account author, Guid postId, byte[] content, string fileName = "anh.jpg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/posts/{postId}/images") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", author.Token);
        return _client.SendAsync(request);
    }

    private async Task SetImageStatusAsync(Guid imageId, CommunityImageStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.CommunityPostImages.Where(i => i.Id == imageId).ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status));
    }

    [Fact]
    public async Task AnUploadedImageLosesItsLocationAndWaitsForReview()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        var uploaded = await DataAsync(await UploadAsync(author, postId, TestImages.JpegWithGps(800, 600)));
        uploaded.GetProperty("status").GetString().Should().Be("pending");
        var imageId = uploaded.GetProperty("id").GetGuid();

        // Chưa duyệt: người khác không thấy, tác giả thấy.
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", reader.Token))).GetProperty("images").GetArrayLength().Should().Be(0);
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", author.Token))).GetProperty("images").GetArrayLength().Should().Be(1);
        (await SendAsync(HttpMethod.Get, $"{Base}/images/{imageId}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var own = await SendAsync(HttpMethod.Get, $"{Base}/images/{imageId}", author.Token);
        own.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đã duyệt: mọi người thấy, và ảnh không còn dấu vết GPS.
        await SetImageStatusAsync(imageId, CommunityImageStatus.Approved);
        var served = await SendAsync(HttpMethod.Get, $"{Base}/images/{imageId}", reader.Token);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        served.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        var bytes = await served.Content.ReadAsByteArrayAsync();
        TestImages.ContainsGpsMarker(bytes).Should().BeFalse();
        TestImages.HasExifSegment(bytes).Should().BeFalse();
        TestImages.IsDecodableJpeg(bytes).Should().BeTrue();
    }

    [Fact]
    public async Task APostWithOnlyAPendingImageStaysOutOfTheFeedUntilApproved()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var post = await SharePlaceAsync(author, UniqueName(), body: null);
        var postId = post.GetProperty("id").GetGuid();
        var imageId = (await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(200, 200)))).GetProperty("id").GetGuid();

        var feed = async () => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts?topic=places&limit=50", reader.Token)))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        (await feed()).Should().NotContain(postId);
        await SetImageStatusAsync(imageId, CommunityImageStatus.Approved);
        (await feed()).Should().Contain(postId);
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("heic")]
    [InlineData("huge")]
    public async Task UnusableFilesAreRefusedWithoutAServerError(string kind)
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var content = kind switch
        {
            "fake" => "đây không phải là ảnh"u8.ToArray(),
            "heic" => TestImages.HeicHeader(),
            _ => TestImages.PngClaiming(9000, 9000),
        };

        var response = await UploadAsync(author, postId, content);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeAsync(response)).Should().Be("COMMUNITY_IMAGE_INVALID");
    }

    [Fact]
    public async Task AFileOverTheSizeLimitIsRefused()
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var response = await UploadAsync(author, postId, new byte[(5 * 1024 * 1024) + 1]);
        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task APostHoldsAtMostThreeImages_AndOnlyTheAuthorCanChangeThem()
    {
        var author = await AccountAsync();
        var stranger = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await UploadAsync(stranger, postId, TestImages.Jpeg(100, 100))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)))).GetProperty("id").GetGuid());
        }

        (await ErrorCodeAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)))).Should().Be("COMMUNITY_LIMIT_REACHED");
        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}/images/{ids[0]}", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}/images/{ids[0]}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.CommunityPostImages.AsNoTracking().SingleAsync(i => i.Id == ids[0])).Data.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletingAndUploadingAgainDoesNotEscapeTheDailyImageLimit()
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        for (var i = 0; i < 10; i++)
        {
            var id = (await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(50, 50)))).GetProperty("id").GetGuid();
            (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}/images/{id}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ErrorCodeAsync(await UploadAsync(author, postId, TestImages.Jpeg(50, 50)))).Should().Be("COMMUNITY_LIMIT_REACHED");
    }

    [Fact]
    public async Task DeletingAPostWipesItsImages()
    {
        var author = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var imageId = (await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)))).GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Get, $"{Base}/images/{imageId}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.CommunityPostImages.AsNoTracking().SingleAsync(i => i.Id == imageId)).Data.Should().BeEmpty();
    }

    private const string Ops = "/api/v1/ops/community";

    private Task<HttpResponseMessage> ReportPostAsync(Account reporter, Guid postId, string reason = "spam")
        => SendAsync(HttpMethod.Post, $"{Base}/posts/{postId}/report", reporter.Token, new { reason });

    private async Task<JsonElement?> FindReportAsync(Account staff, Guid targetId)
    {
        var data = await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/reports?limit=200", staff.Token));
        foreach (var item in data.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("targetId").GetGuid() == targetId)
            {
                return item;
            }
        }

        return null;
    }

    [Fact]
    public async Task EnoughDifferentReportersHideAPost_AndRepeatsDoNotCount()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var first = await AccountAsync();

        (await ReportPostAsync(first, postId)).IsSuccessStatusCode.Should().BeTrue();
        (await ReportPostAsync(first, postId)).IsSuccessStatusCode.Should().BeTrue();
        (await ReportPostAsync(first, postId, "scam")).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await ReportPostAsync(await AccountAsync(), postId);
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await ReportPostAsync(await AccountAsync(), postId, "harassment");
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ASelfHarmReportGoesFirstAndDoesNotHideThePost()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var normal = (await ShareExperienceAsync(author, "Bài thường")).GetProperty("id").GetGuid();
        await ReportPostAsync(await AccountAsync(), normal, "spam");

        var urgent = (await DataAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "difficulty", body = "Mình không còn thấy lối thoát" }))).GetProperty("id").GetGuid();
        for (var i = 0; i < 3; i++)
        {
            (await ReportPostAsync(await AccountAsync(), urgent, "self_harm")).IsSuccessStatusCode.Should().BeTrue();
        }

        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{urgent}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = (await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/reports?limit=200", staff.Token))).GetProperty("items");
        queue[0].GetProperty("priority").GetString().Should().Be("urgent");
        queue[0].GetProperty("reason").GetString().Should().Be("self_harm");
        (await FindReportAsync(staff, normal))!.Value.GetProperty("priority").GetString().Should().Be("normal");
    }

    [Fact]
    public async Task ReportsRejectBadInputAndOwnContent()
    {
        var author = await AccountAsync();
        var reporter = await AccountAsync();
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await ErrorCodeAsync(await ReportPostAsync(reporter, postId, "khong-co"))).Should().Be("COMMUNITY_INVALID_INPUT");
        (await ErrorCodeAsync(await ReportPostAsync(author, postId))).Should().Be("COMMUNITY_OWN_POST");
        (await ReportPostAsync(reporter, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CommentsCanBeReportedHiddenAndRestored()
    {
        var author = await AccountAsync();
        var commenter = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var commentId = (await CommentAsync(commenter, postId)).GetProperty("id").GetGuid();

        var count = async () => (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}/comments", author.Token))).GetProperty("items").GetArrayLength();
        for (var i = 0; i < 3; i++)
        {
            (await SendAsync(HttpMethod.Post, $"{Base}/comments/{commentId}/report", (await AccountAsync()).Token, new { reason = "harassment" })).IsSuccessStatusCode.Should().BeTrue();
        }

        (await count()).Should().Be(0);
        (await SendAsync(HttpMethod.Post, $"{Ops}/comments/{commentId}/restore", staff.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await count()).Should().Be(1);
        (await FindReportAsync(staff, commentId)).Should().BeNull();

        (await SendAsync(HttpMethod.Post, $"{Ops}/comments/{commentId}/hide", staff.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await count()).Should().Be(0);
    }

    [Fact]
    public async Task ResolvingAReportRemovesOrRestoresTheContentAndClosesTheOthers()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);

        var upheld = (await ShareExperienceAsync(author, "Bài vi phạm")).GetProperty("id").GetGuid();
        await ReportPostAsync(await AccountAsync(), upheld);
        await ReportPostAsync(await AccountAsync(), upheld);
        var report = (await FindReportAsync(staff, upheld))!.Value.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, $"{Ops}/reports/{report}/resolve", staff.Token, new { decision = "upheld" })).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{upheld}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await FindReportAsync(staff, upheld)).Should().BeNull();
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/reports/{report}/resolve", staff.Token, new { decision = "upheld" }))).Should().Be("COMMUNITY_ALREADY_RESOLVED");

        var dismissed = (await ShareExperienceAsync(author, "Bài bị báo cáo oan")).GetProperty("id").GetGuid();
        for (var i = 0; i < 3; i++)
        {
            await ReportPostAsync(await AccountAsync(), dismissed);
        }

        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{dismissed}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var other = (await FindReportAsync(staff, dismissed))!.Value.GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, $"{Ops}/reports/{other}/resolve", staff.Token, new { decision = "dismissed" })).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{dismissed}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StaffNeverSeeWhoWroteAnonymousContent()
    {
        var author = await AccountAsync("Tên Rất Riêng");
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var reporter = await AccountAsync("Người Báo Cáo");
        await ReportPostAsync(reporter, postId);
        await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)));

        foreach (var url in new[] { $"{Ops}/reports?limit=200", $"{Ops}/images?limit=200" })
        {
            var raw = await (await SendAsync(HttpMethod.Get, url, staff.Token)).Content.ReadAsStringAsync();
            raw.Should().NotContain(author.Id.ToString()).And.NotContain(author.Email).And.NotContain("Tên Rất Riêng");
            raw.Should().NotContain(reporter.Id.ToString()).And.NotContain("Người Báo Cáo");
        }
    }

    [Fact]
    public async Task StaffApproveOrRejectImages_AndRejectedDataIsGone()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        var approved = (await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)))).GetProperty("id").GetGuid();
        var rejected = (await DataAsync(await UploadAsync(author, postId, TestImages.Jpeg(100, 100)))).GetProperty("id").GetGuid();

        var queue = await DataAsync(await SendAsync(HttpMethod.Get, $"{Ops}/images?limit=200", staff.Token));
        queue.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(new[] { approved, rejected });

        // Nhân viên xem được ảnh chờ duyệt để quyết định.
        (await SendAsync(HttpMethod.Get, $"{Ops}/images/{approved}", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, $"{Ops}/images/{approved}/approve", staff.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Post, $"{Ops}/images/{rejected}/reject", staff.Token)).IsSuccessStatusCode.Should().BeTrue();

        (await SendAsync(HttpMethod.Get, $"{Base}/images/{approved}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"{Base}/images/{rejected}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/images/{approved}/approve", staff.Token))).Should().Be("COMMUNITY_ALREADY_RESOLVED");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.CommunityPostImages.AsNoTracking().SingleAsync(i => i.Id == rejected)).Data.Should().BeEmpty();

        var events = await db.AuditLogs.AsNoTracking().Where(l => l.UserId == staff.Id).Select(l => l.EventType).ToListAsync();
        events.Should().Contain("Ops.Community.ImageApproved").And.Contain("Ops.Community.ImageRejected");
    }

    [Fact]
    public async Task OrdinaryUsersCannotModerate_AndOnlyManagersCanMute()
    {
        var author = await AccountAsync();
        var user = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var manager = await AccountAsync("Quản lý", role: UserRole.OpsManager);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Get, $"{Ops}/reports", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{Ops}/posts/{postId}/hide", user.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{Ops}/posts/{postId}/mute-author", staff.Token, new { days = 7 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var muted = await SendAsync(HttpMethod.Post, $"{Ops}/posts/{postId}/mute-author", manager.Token, new { days = 7 });
        muted.IsSuccessStatusCode.Should().BeTrue();
        (await muted.Content.ReadAsStringAsync()).Should().NotContain(author.Id.ToString());

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Base}/posts", author.Token, new { topic = "experience", body = "Một chia sẻ" }))).Should().Be("COMMUNITY_MUTED");
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, $"{Ops}/posts/{postId}/mute-author", manager.Token, new { days = 0 }))).Should().Be("COMMUNITY_INVALID_INPUT");
    }

    [Fact]
    public async Task HidingAPlaceHidesItsPosts()
    {
        var author = await AccountAsync();
        var reader = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var post = await SharePlaceAsync(author, UniqueName());
        var placeId = post.GetProperty("place").GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, $"{Ops}/places/{placeId}/hide", staff.Token)).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(HttpMethod.Get, $"{Base}/posts/{post.GetProperty("id").GetGuid()}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, $"{Base}/places/{placeId}", reader.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingReportedContentClosesItsReports()
    {
        var author = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        await ReportPostAsync(await AccountAsync(), postId);
        (await FindReportAsync(staff, postId)).Should().NotBeNull();

        (await SendAsync(HttpMethod.Delete, $"{Base}/posts/{postId}", author.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FindReportAsync(staff, postId)).Should().BeNull();
    }

    [Fact]
    public async Task HiddenContentCannotBeEditedByItsAuthor()
    {
        var author = await AccountAsync();
        var staff = await AccountAsync("Nhân viên", role: UserRole.Staff);
        var postId = (await ShareExperienceAsync(author)).GetProperty("id").GetGuid();
        (await SendAsync(HttpMethod.Post, $"{Ops}/posts/{postId}/hide", staff.Token)).IsSuccessStatusCode.Should().BeTrue();

        (await ErrorCodeAsync(await SendAsync(new HttpMethod("PATCH"), $"{Base}/posts/{postId}", author.Token, new { body = "Đã sửa" }))).Should().Be("COMMUNITY_POST_NOT_EDITABLE");
        (await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/posts/{postId}", author.Token))).GetProperty("status").GetString().Should().Be("removed");
    }

    private async Task<JsonElement> PromptAsync(Account user, Guid transactionId)
        => await DataAsync(await SendAsync(HttpMethod.Get, $"{Base}/share-prompt?transactionId={transactionId}", user.Token));

    [Fact]
    public async Task SpendingAtAFoodPlacePromptsOnceADay()
    {
        var user = await AccountAsync();
        var first = await PromptAsync(user, await SeedDebitAsync(user, "Quán cơm Bà Ba"));
        first.GetProperty("shouldPrompt").GetBoolean().Should().BeTrue();
        first.GetProperty("suggestedPlaceName").GetString().Should().Be("Quán cơm Bà Ba");
        first.GetProperty("suggestedPriceCents").GetInt64().Should().Be(30_000);

        (await PromptAsync(user, await SeedDebitAsync(user, "Quán phở Hai Lúa"))).GetProperty("shouldPrompt").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ACategoryTheUserNamedThemselvesStillCounts()
    {
        var user = await AccountAsync();
        var prompt = await PromptAsync(user, await SeedDebitAsync(user, "DO AN TRUA QUAN COM BA BA", categoryName: "Sinh hoạt"));
        prompt.GetProperty("shouldPrompt").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("Điện lực TP HCM", 30_000L, TransactionType.Debit, TransactionStatus.Confirmed)]
    [InlineData("Nguyễn Văn An", 30_000L, TransactionType.Debit, TransactionStatus.Confirmed)]
    [InlineData("Quán cơm Bà Ba", 300_001L, TransactionType.Debit, TransactionStatus.Confirmed)]
    [InlineData("Quán cơm Bà Ba", 30_000L, TransactionType.Credit, TransactionStatus.Confirmed)]
    [InlineData("Quán cơm Bà Ba", 30_000L, TransactionType.Debit, TransactionStatus.Draft)]
    public async Task OtherSpendingDoesNotPrompt(string merchant, long amount, TransactionType type, TransactionStatus status)
    {
        var user = await AccountAsync();
        var prompt = await PromptAsync(user, await SeedDebitAsync(user, merchant, amountCents: amount, type: type, status: status));
        prompt.GetProperty("shouldPrompt").GetBoolean().Should().BeFalse();
        prompt.GetProperty("suggestedPlaceName").ValueKind.Should().Be(JsonValueKind.Null);
        prompt.GetProperty("suggestedPriceCents").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task AnotherPersonsTransactionIsNotFound()
    {
        var owner = await AccountAsync();
        var stranger = await AccountAsync();
        var transaction = await SeedDebitAsync(owner, "Quán cơm Bà Ba");
        (await SendAsync(HttpMethod.Get, $"{Base}/share-prompt?transactionId={transaction}", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TurningPromptsOffStopsThem()
    {
        var user = await AccountAsync();
        (await SendAsync(new HttpMethod("PATCH"), $"{Base}/me", user.Token, new { promptsEnabled = false })).IsSuccessStatusCode.Should().BeTrue();
        (await PromptAsync(user, await SeedDebitAsync(user, "Quán cơm Bà Ba"))).GetProperty("shouldPrompt").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task APlaceAlreadySharedIsNotAskedAboutAgain()
    {
        var user = await AccountAsync();
        var name = "Quán cơm " + UniqueWord();
        await SharePlaceAsync(user, name);
        (await PromptAsync(user, await SeedDebitAsync(user, name))).GetProperty("shouldPrompt").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PlacesOthersSharedNearbyAreOfferedSoNamesDoNotSplit()
    {
        var other = await AccountAsync();
        var user = await AccountAsync();
        var name = "Quán cơm " + UniqueWord();
        await SharePlaceAsync(other, name);
        (await SendAsync(new HttpMethod("PATCH"), $"{Base}/me", user.Token, new { provinceCode = Hcm, areaCode = District7 })).IsSuccessStatusCode.Should().BeTrue();

        var prompt = await PromptAsync(user, await SeedDebitAsync(user, name.ToUpperInvariant()));
        prompt.GetProperty("existingPlaces").GetArrayLength().Should().Be(1);
        prompt.GetProperty("existingPlaces")[0].GetProperty("name").GetString().Should().Be(name);
    }
}
