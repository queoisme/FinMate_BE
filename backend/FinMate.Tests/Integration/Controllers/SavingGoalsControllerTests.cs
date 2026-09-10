using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class SavingGoalsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public SavingGoalsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record GoalData(
        Guid Id, string Name, long TargetCents, long SavedCents, long RemainingCents,
        int PercentComplete, string Status, DateTimeOffset? Deadline, DateTimeOffset? CompletedAt);
    private record GoalBody(bool Success, GoalData Data);
    private record GoalListBody(bool Success, List<GoalData> Data);
    private record ContributionData(Guid Id, long AmountCents, string? Note, DateTimeOffset ContributedAt);
    private record ProgressData(
        Guid GoalId, string Name, string Status, long TargetCents, long SavedCents, long RemainingCents,
        int PercentComplete, DateTimeOffset? Deadline, int? DaysRemaining, long? RequiredPerDayCents,
        bool IsOnTrack, List<ContributionData> RecentContributions);
    private record ProgressBody(bool Success, ProgressData Data);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Goal Test User"));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return loginBody!.Data.AccessToken;
    }

    private HttpRequestMessage AuthedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> PostAsync(string url, string accessToken, object? body = null)
    {
        var request = AuthedRequest(HttpMethod.Post, url, accessToken);
        request.Content = JsonContent.Create(body ?? new { });
        return await _client.SendAsync(request);
    }

    private async Task<GoalData> CreateGoalAsync(string accessToken, long targetCents, DateTimeOffset? deadline = null)
    {
        var response = await PostAsync("/api/v1/saving-goals", accessToken,
            new { name = "Mua laptop", targetCents, deadline });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GoalBody>(JsonOptions))!.Data;
    }

    [Fact]
    public async Task ContributeFlow_AccumulatesThenAutoCompletesAtTarget()
    {
        var accessToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(accessToken, 10_000_000, DateTimeOffset.UtcNow.AddDays(90));
        goal.Status.Should().Be("active");
        goal.SavedCents.Should().Be(0);

        var first = await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken,
            new { amountCents = 3_000_000L, note = "Lương tháng 9" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterFirst = (await first.Content.ReadFromJsonAsync<GoalBody>(JsonOptions))!.Data;
        afterFirst.SavedCents.Should().Be(3_000_000);
        afterFirst.PercentComplete.Should().Be(30);
        afterFirst.Status.Should().Be("active");

        var second = await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken,
            new { amountCents = 7_000_000L, note = (string?)null });
        var afterSecond = (await second.Content.ReadFromJsonAsync<GoalBody>(JsonOptions))!.Data;
        afterSecond.SavedCents.Should().Be(10_000_000);
        afterSecond.RemainingCents.Should().Be(0);
        afterSecond.Status.Should().Be("completed");
        afterSecond.CompletedAt.Should().NotBeNull();

        // Mục tiêu đã hoàn thành thì không nhận thêm đóng góp.
        var third = await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken,
            new { amountCents = 1_000L });
        third.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await third.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions);
        error!.Error.Code.Should().Be("SAVING_GOAL_NOT_ACTIVE");
    }

    [Fact]
    public async Task GetProgress_ReturnsContributionHistoryAndOnTrackState()
    {
        var accessToken = await RegisterAndLoginAsync();
        // Vừa tạo hôm nay, deadline còn 100 ngày → tiến độ kỳ vọng gần 0, chưa góp vẫn on-track.
        var goal = await CreateGoalAsync(accessToken, 10_000_000, DateTimeOffset.UtcNow.AddDays(100));

        var freshResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/saving-goals/{goal.Id}/progress", accessToken));
        freshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = (await freshResponse.Content.ReadFromJsonAsync<ProgressBody>(JsonOptions))!.Data;
        fresh.IsOnTrack.Should().BeTrue();
        fresh.DaysRemaining.Should().BeInRange(99, 101);
        fresh.RequiredPerDayCents.Should().BeInRange(99_000, 101_000);
        fresh.RecentContributions.Should().BeEmpty();

        await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken,
            new { amountCents = 2_500_000L, note = "Thưởng" });

        var afterResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/saving-goals/{goal.Id}/progress", accessToken));
        var after = (await afterResponse.Content.ReadFromJsonAsync<ProgressBody>(JsonOptions))!.Data;
        after.SavedCents.Should().Be(2_500_000);
        after.PercentComplete.Should().Be(25);
        after.RemainingCents.Should().Be(7_500_000);
        after.RecentContributions.Should().HaveCount(1);
        after.RecentContributions[0].AmountCents.Should().Be(2_500_000);
        after.RecentContributions[0].Note.Should().Be("Thưởng");
    }

    [Fact]
    public async Task GetProgress_NoDeadline_HasNoPaceRequirement()
    {
        var accessToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(accessToken, 10_000_000);

        var response = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, $"/api/v1/saving-goals/{goal.Id}/progress", accessToken));
        var progress = (await response.Content.ReadFromJsonAsync<ProgressBody>(JsonOptions))!.Data;

        progress.IsOnTrack.Should().BeTrue();
        progress.Deadline.Should().BeNull();
        progress.DaysRemaining.Should().BeNull();
        progress.RequiredPerDayCents.Should().BeNull();
    }

    [Fact]
    public async Task Cancel_MovesGoalToCancelledAndKeepsContributionHistory()
    {
        var accessToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(accessToken, 10_000_000);
        await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken, new { amountCents = 1_000_000L });

        var cancelResponse = await PostAsync($"/api/v1/saving-goals/{goal.Id}/cancel", accessToken);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<GoalBody>(JsonOptions))!.Data;
        cancelled.Status.Should().Be("cancelled");
        // Hủy không xóa phần đã góp.
        cancelled.SavedCents.Should().Be(1_000_000);

        // Hủy hai lần không hợp lệ.
        (await PostAsync($"/api/v1/saving-goals/{goal.Id}/cancel", accessToken))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_LoweringTargetBelowSaved_AutoCompletesGoal()
    {
        var accessToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(accessToken, 10_000_000);
        await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken, new { amountCents = 4_000_000L });

        var patchRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/saving-goals/{goal.Id}", accessToken);
        patchRequest.Content = JsonContent.Create(new { name = "Mua tai nghe", targetCents = 3_000_000L, deadline = (DateTimeOffset?)null });
        var patchResponse = await _client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = (await patchResponse.Content.ReadFromJsonAsync<GoalBody>(JsonOptions))!.Data;
        updated.Name.Should().Be("Mua tai nghe");
        updated.Status.Should().Be("completed");
    }

    [Fact]
    public async Task GetList_FiltersByStatus()
    {
        var accessToken = await RegisterAndLoginAsync();
        var activeGoal = await CreateGoalAsync(accessToken, 10_000_000);
        var cancelledGoal = await CreateGoalAsync(accessToken, 5_000_000);
        await PostAsync($"/api/v1/saving-goals/{cancelledGoal.Id}/cancel", accessToken);

        var allResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, "/api/v1/saving-goals", accessToken));
        var all = (await allResponse.Content.ReadFromJsonAsync<GoalListBody>(JsonOptions))!.Data;
        all.Should().HaveCount(2);

        var activeResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Get, "/api/v1/saving-goals?status=Active", accessToken));
        var active = (await activeResponse.Content.ReadFromJsonAsync<GoalListBody>(JsonOptions))!.Data;
        active.Should().ContainSingle().Which.Id.Should().Be(activeGoal.Id);
    }

    [Fact]
    public async Task Contribute_ToAnotherUsersGoal_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(ownerToken, 10_000_000);

        var otherToken = await RegisterAndLoginAsync();
        var response = await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", otherToken,
            new { amountCents = 1_000L });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_PastDeadline_ReturnsBadRequest()
    {
        var accessToken = await RegisterAndLoginAsync();

        var response = await PostAsync("/api/v1/saving-goals", accessToken,
            new { name = "Quá khứ", targetCents = 1_000_000L, deadline = DateTimeOffset.UtcNow.AddDays(-1) });

        // Lỗi validator là 400; 422 dành cho vi phạm quy tắc nghiệp vụ (SAVING_GOAL_NOT_ACTIVE).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Contribute_NonPositiveAmount_ReturnsBadRequest()
    {
        var accessToken = await RegisterAndLoginAsync();
        var goal = await CreateGoalAsync(accessToken, 10_000_000);

        var response = await PostAsync($"/api/v1/saving-goals/{goal.Id}/contribute", accessToken,
            new { amountCents = 0L });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
