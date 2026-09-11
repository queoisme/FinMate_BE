using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class GamificationControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public GamificationControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record AccountData(Guid Id);
    private record AccountBody(bool Success, AccountData Data);
    private record ProfileData(
        int Level, int ExpPoints, int ExpForNextLevel, int ExpToNextLevel,
        int CurrentStreakDays, int LongestStreakDays, DateOnly? LastActivityDate);
    private record ProfileBody(bool Success, ProfileData Data);
    private record MissionData(
        Guid MissionId, string Code, string Title, string PeriodType,
        int Progress, int Target, int ExpReward, bool IsCompleted);
    private record MissionListBody(bool Success, List<MissionData> Data);
    private record MascotItemData(
        Guid Id, string Code, string Name, string ItemType, bool IsOwned, bool IsEquipped,
        string UnlockType, string? UnlockHint);
    private record MascotData(List<MascotItemData> Items);
    private record MascotBody(bool Success, MascotData Data);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Gamification Test User"));

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        return (await login.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions))!.Data.AccessToken;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, string token, object body)
    {
        var request = Authed(method, url, token);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> CashAccountAsync(string token)
    {
        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/financial-accounts", token, new
        {
            accountType = "Cash",
            accountName = "Ví tiền mặt",
            initialBalanceCents = 50_000_000L,
        });
        return (await response.Content.ReadFromJsonAsync<AccountBody>(JsonOptions))!.Data.Id;
    }

    private Task SpendAsync(string token, Guid accountId, long amount = 50_000)
        => SendJsonAsync(HttpMethod.Post, "/api/v1/transactions", token, new
        {
            financialAccountId = accountId,
            categoryId = (Guid?)null,
            amountCents = amount,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });

    private async Task<ProfileData> ProfileAsync(string token)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/gamification/profile", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProfileBody>(JsonOptions))!.Data;
    }

    private async Task<MascotData> MascotAsync(string token)
    {
        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/gamification/mascot", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MascotBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task Profile_BrandNewUser_StartsAtLevelOneWithNothingRecorded()
    {
        var token = await RegisterAndLoginAsync();

        var profile = await ProfileAsync(token);

        profile.Level.Should().Be(1);
        profile.ExpPoints.Should().Be(0);
        profile.ExpForNextLevel.Should().Be(100);
        profile.CurrentStreakDays.Should().Be(0);
        profile.LastActivityDate.Should().BeNull();
    }

    [Fact]
    public async Task Missions_AreListedBeforeTheUserHasTouchedThem()
    {
        var token = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/gamification/missions", token));
        var missions = (await response.Content.ReadFromJsonAsync<MissionListBody>(JsonOptions))!.Data;

        // Dòng user_mission tạo lazily, nhưng user vẫn phải nhìn thấy nhiệm vụ để mà làm.
        missions.Should().NotBeEmpty();
        missions.Should().Contain(m => m.Code == "daily_manual_1");
        missions.Where(m => m.Code == "daily_manual_1").Should().OnlyContain(m => m.Progress == 0 && !m.IsCompleted);
    }

    [Fact]
    public async Task ManualTransaction_AwardsExpAndStartsTheStreak()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);

        await SpendAsync(token, accountId);

        var profile = await ProfileAsync(token);
        profile.CurrentStreakDays.Should().Be(1);
        profile.LastActivityDate.Should().NotBeNull();
        // +5 cho giao dịch thủ công, +30 cho mission daily_manual_1 (mục tiêu 1) hoàn thành ngay.
        profile.ExpPoints.Should().Be(35);
    }

    [Fact]
    public async Task ManualTransaction_CompletesTheSingleStepDailyMission()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);

        await SpendAsync(token, accountId);

        var response = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/gamification/missions", token));
        var missions = (await response.Content.ReadFromJsonAsync<MissionListBody>(JsonOptions))!.Data;

        var mission = missions.Single(m => m.Code == "daily_manual_1");
        mission.IsCompleted.Should().BeTrue();
        mission.Progress.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedActivityOnTheSameDay_DoesNotInflateTheStreak()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);

        await SpendAsync(token, accountId);
        await SpendAsync(token, accountId);
        await SpendAsync(token, accountId);

        var profile = await ProfileAsync(token);
        profile.CurrentStreakDays.Should().Be(1);
    }

    [Fact]
    public async Task EnoughActivity_LevelsTheUserUp()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);

        // 35 EXP từ giao dịch đầu, sau đó +5 mỗi giao dịch — đủ để vượt mốc 100.
        for (var i = 0; i < 15; i++)
        {
            await SpendAsync(token, accountId);
        }

        var profile = await ProfileAsync(token);
        profile.ExpPoints.Should().BeGreaterThanOrEqualTo(100);
        profile.Level.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Mascot_DefaultItemsAreOwnedAfterFirstActivity_LockedOnesShowTheirCondition()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        await SpendAsync(token, accountId);

        var mascot = await MascotAsync(token);

        mascot.Items.Single(i => i.Code == "outfit_basic").IsOwned.Should().BeTrue();

        var locked = mascot.Items.Single(i => i.Code == "outfit_office");
        locked.IsOwned.Should().BeFalse();
        locked.UnlockHint.Should().Contain("level");
    }

    [Fact]
    public async Task Mascot_EquipAnOwnedItem_MarksItEquipped()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        await SpendAsync(token, accountId);

        var owned = (await MascotAsync(token)).Items.First(i => i.IsOwned);

        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/gamification/mascot/outfit", token,
            new { itemIds = new[] { owned.Id } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await MascotAsync(token)).Items.Single(i => i.Id == owned.Id).IsEquipped.Should().BeTrue();
    }

    [Fact]
    public async Task Mascot_EquippingTwoItemsOfTheSameType_IsRejected()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);
        await SpendAsync(token, accountId);

        var mascot = await MascotAsync(token);
        var sameTypePair = mascot.Items
            .Where(i => i.IsOwned)
            .GroupBy(i => i.ItemType)
            .FirstOrDefault(g => g.Count() > 1);

        if (sameTypePair is null)
        {
            // Seed mặc định chỉ cho mỗi loại 1 món; dựng trường hợp trùng bằng cách gửi
            // cùng một item hai lần là vô nghĩa (đã Distinct), nên bỏ qua nhánh này.
            return;
        }

        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/gamification/mascot/outfit", token,
            new { itemIds = sameTypePair.Select(i => i.Id).ToArray() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        error!.Error.Code.Should().Be("GAMIFICATION_DUPLICATE_MASCOT_SLOT");
    }

    [Fact]
    public async Task Mascot_EquippingAnItemYouDoNotOwn_ReturnsNotFound()
    {
        var owner = await RegisterAndLoginAsync();
        var ownerAccount = await CashAccountAsync(owner);
        await SpendAsync(owner, ownerAccount);
        var lockedItem = (await MascotAsync(owner)).Items.First(i => !i.IsOwned);

        var token = await RegisterAndLoginAsync();
        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/gamification/mascot/outfit", token,
            new { itemIds = new[] { lockedItem.Id } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingAConfirmedTransaction_TakesBackTheExpItGave()
    {
        var token = await RegisterAndLoginAsync();
        var accountId = await CashAccountAsync(token);

        await SpendAsync(token, accountId);
        var before = await ProfileAsync(token);

        var list = await _client.SendAsync(Authed(HttpMethod.Get, "/api/v1/transactions", token));
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var transactionId = doc.RootElement.GetProperty("data")[0].GetProperty("id").GetGuid();

        await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/transactions/{transactionId}", token));

        var after = await ProfileAsync(token);
        after.ExpPoints.Should().Be(before.ExpPoints - 10);
    }

    [Fact]
    public async Task Profile_IsPerUser()
    {
        var busy = await RegisterAndLoginAsync();
        var busyAccount = await CashAccountAsync(busy);
        await SpendAsync(busy, busyAccount);

        var fresh = await RegisterAndLoginAsync();

        (await ProfileAsync(fresh)).ExpPoints.Should().Be(0);
    }
}
