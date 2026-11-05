using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Báo cáo nhóm và các quyết định về dữ liệu khi rời nhóm, yêu cầu xóa tài khoản, xóa cứng.</summary>
[Collection("Integration")]
public class GroupPrivacyTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public Task InitializeAsync() => _flagFactory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private readonly AuthApiFactory _flagFactory;

    public GroupPrivacyTests(AuthApiFactory factory)
    {
        _flagFactory = factory;
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private record Wrapper<T>(bool Success, T Data);
    private record AuthData(string AccessToken);
    private record IdData(Guid Id);
    private record CategoryData(Guid Id, string Slug, bool IsSystem);
    private record InvitationData(string? Token);
    private record MemberData(Guid UserId, string DisplayName, string Role, bool IsActive);
    private record BudgetData(Guid Id, long SpentCents);
    private record GoalData(Guid Id, long SavedCents, string Status);
    private record GroupData(Guid Id);
    private record CategorySpend(Guid? CategoryId, Guid? GroupCategoryId, string CategoryName, long SpentCents);
    private record MemberTotals(Guid UserId, string MemberName, long SpentCents, long IncomeCents);
    private record Report(long TotalSpentCents, long TotalIncomeCents, List<CategorySpend> ByCategory, List<MemberTotals> ByMember);
    private record TestUser(Guid Id, string Email, string Token, Guid AccountId);

    private const string Password = "Password123!";

    private async Task<TestUser> NewUserAsync(string name = "Người dùng")
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, name));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token);
        var account = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token, new { accountType = "Cash", accountName = "Ví", initialBalanceCents = 50_000_000L });
        return new TestUser(me.Id, email, token, account.Id);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string url, string token, object? body = null)
    {
        var response = await SendAsync(method, url, token, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Wrapper<T>>(JsonOptions))!.Data;
    }

    private async Task<(TestUser Owner, Guid GroupId)> NewGroupAsync(string ownerName = "Chủ nhóm")
    {
        var owner = await NewUserAsync(ownerName);
        return (owner, (await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Gia đình" })).Id);
    }

    private async Task<TestUser> JoinAsync(TestUser inviter, Guid groupId, string role = "Member", string name = "Thành viên")
    {
        var user = await NewUserAsync(name);
        var invite = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", inviter.Token, new { role });
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, new { token = invite.Token });
        return user;
    }

    private Task<IdData> SpendAsync(TestUser user, long amount, Guid? categoryId = null, Guid? groupId = null, string type = "Debit")
        => ReadAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", user.Token, new
        {
            financialAccountId = user.AccountId,
            categoryId,
            amountCents = amount,
            transactionType = type,
            transactedAt = DateTimeOffset.UtcNow,
            groupId,
        });

    private async Task<Guid> SystemCategoryAsync(string token, string slug)
        => (await ReadAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token)).First(c => c.IsSystem && c.Slug == slug).Id;

    private async Task RequestDeletionAsync(TestUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/account");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        request.Content = JsonContent.Create(new { password = Password });
        var response = await _client.SendAsync(request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    private async Task HardDeleteNowAsync(TestUser user)
    {
        await RequestDeletionAsync(user);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        await db.DataDeletionRequests.IgnoreQueryFilters().Where(r => r.UserId == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ScheduledHardDeleteAt, DateTimeOffset.UtcNow.AddDays(-1)));
        await scope.ServiceProvider.GetRequiredService<DataDeletionJob>().RunAsync();
    }

    [Fact]
    public async Task TheMonthlyReportTotalsByCategoryAndMember_AndExcludesWhatIsNotSpending()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var food = await SystemCategoryAsync(owner.Token, "food");
        var loan = await SystemCategoryAsync(owner.Token, "loan");
        var privateCategory = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/categories", member.Token, new { name = "Bí mật" });

        await SpendAsync(owner, 100_000, food, groupId);
        await SpendAsync(member, 40_000, food, groupId);
        await SpendAsync(member, 25_000, privateCategory.Id, groupId);
        await SpendAsync(member, 900_000, food);                // không chia sẻ
        await SpendAsync(owner, 800_000, loan);                 // vay/mượn không chia sẻ được
        await SpendAsync(owner, 500_000, groupId: groupId, type: "Credit");

        var report = await ReadAsync<Report>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", member.Token);

        report.TotalSpentCents.Should().Be(165_000);
        report.TotalIncomeCents.Should().Be(500_000);
        report.ByCategory.Single(c => c.CategoryId == food).SpentCents.Should().Be(140_000);
        report.ByCategory.Should().NotContain(c => c.CategoryName == "Bí mật");
        report.ByCategory.Single(c => c.CategoryName == "Chưa phân loại").SpentCents.Should().Be(25_000);
        report.ByMember.Single(m => m.UserId == owner.Id).SpentCents.Should().Be(100_000);
        report.ByMember.Single(m => m.UserId == member.Id).SpentCents.Should().Be(65_000);
    }

    [Fact]
    public async Task SummaryModeKeepsTotalsVisible_AndOutsidersGet404()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var stranger = await NewUserAsync();
        await SpendAsync(owner, 10_000, groupId: groupId);
        await ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}", owner.Token, new { transactionVisibility = "Summary" });

        (await ReadAsync<Report>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", member.Token)).TotalSpentCents.Should().Be(10_000);
        (await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", stranger.Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RequestingDeletionHandsOwnershipToTheLongestServingManager_AndTheMemberStaysListedAnonymously()
    {
        var (owner, groupId) = await NewGroupAsync("Tên thật của chủ");
        var member = await JoinAsync(owner, groupId);
        var firstManager = await JoinAsync(owner, groupId, "Manager");
        await JoinAsync(owner, groupId, "Manager");

        await RequestDeletionAsync(owner);

        var members = await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/members", member.Token);
        members.Should().ContainSingle(m => m.Role == "Owner").Which.UserId.Should().Be(firstManager.Id);
        var departed = members.Single(m => m.UserId == owner.Id);
        departed.IsActive.Should().BeFalse();
        departed.DisplayName.Should().NotContain("Tên thật");
    }

    [Fact]
    public async Task WithNoManagerOwnershipGoesToTheLongestServingMember()
    {
        var (owner, groupId) = await NewGroupAsync();
        var first = await JoinAsync(owner, groupId);
        await JoinAsync(owner, groupId);

        await RequestDeletionAsync(owner);

        (await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/members", first.Token))
            .Single(m => m.Role == "Owner").UserId.Should().Be(first.Id);
    }

    [Fact]
    public async Task CancellingTheDeletionKeepsTheMembership_ButDoesNotGiveOwnershipBack()
    {
        var (owner, groupId) = await NewGroupAsync("Chủ cũ");
        var manager = await JoinAsync(owner, groupId, "Manager");
        await RequestDeletionAsync(owner);

        var adminLogin = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword));
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var requestId = await db.DataDeletionRequests.IgnoreQueryFilters().Where(r => r.UserId == owner.Id).Select(r => r.Id).SingleAsync();
            (await SendAsync(HttpMethod.Post, $"/api/v1/admin/data-deletion-requests/{requestId}/cancel", adminToken, new { reason = "đổi ý" })).IsSuccessStatusCode.Should().BeTrue();
        }

        var members = await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/members", manager.Token);
        members.Single(m => m.Role == "Owner").UserId.Should().Be(manager.Id);
        var returned = members.Single(m => m.UserId == owner.Id);
        returned.IsActive.Should().BeTrue();
        returned.DisplayName.Should().Be("Chủ cũ");
        returned.Role.Should().Be("Manager");
    }

    [Fact]
    public async Task HardDeletingAMemberRemovesTheirMoneyFromTheGroup_AndKeepsEveryoneElses()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var food = await SystemCategoryAsync(owner.Token, "food");
        var total = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 100_000_000L });
        var foodBudget = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = food, limitCents = 100_000_000L });
        var goal = await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", owner.Token, new { name = "Du lịch", targetCents = 10_000_000L });
        await SpendAsync(owner, 100_000, food, groupId);
        await SpendAsync(member, 60_000, food, groupId);
        await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", owner.Token, new { amountCents = 30_000L });
        await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", member.Token, new { amountCents = 20_000L });

        await HardDeleteNowAsync(member);

        var budgets = await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token);
        budgets.Single(b => b.Id == total.Id).SpentCents.Should().Be(100_000);
        budgets.Single(b => b.Id == foodBudget.Id).SpentCents.Should().Be(100_000);
        (await ReadAsync<List<GoalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/goals", owner.Token)).Single().SavedCents.Should().Be(30_000);
        (await ReadAsync<Report>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", owner.Token)).ByMember.Should().ContainSingle(m => m.UserId == owner.Id);
        (await ReadAsync<List<MemberData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/members", owner.Token)).Should().ContainSingle();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == member.Id)).Should().BeFalse();
        (await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.UserId == member.Id)).Should().BeFalse();
        (await db.GoalContributions.AnyAsync(c => c.UserId == member.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HardDeletingTheCreatorKeepsGroupBudgetsAndGoals_AndACompletedGoalStaysCompleted()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId, "Manager");
        var budget = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 100_000_000L });
        var goal = await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", owner.Token, new { name = "Du lịch", targetCents = 50_000L });
        await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", owner.Token, new { amountCents = 50_000L });

        await HardDeleteNowAsync(owner);

        (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", member.Token)).Should().ContainSingle(b => b.Id == budget.Id);
        var kept = (await ReadAsync<List<GoalData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/goals", member.Token)).Single();
        kept.Status.Should().BeEquivalentTo("Completed");
        kept.SavedCents.Should().Be(0);
    }

    [Fact]
    public async Task HardDeletingTheLastMemberSoftDeletesTheGroup()
    {
        var (owner, groupId) = await NewGroupAsync();
        await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 1000L });

        await HardDeleteNowAsync(owner);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var group = await db.Groups.IgnoreQueryFilters().SingleAsync(g => g.Id == groupId);
        group.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ALeaverDeletingTheirAccountStillRemovesTheirSharedMoney()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var budget = await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 100_000_000L });
        await SpendAsync(member, 70_000, groupId: groupId);
        (await SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", member.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await HardDeleteNowAsync(member);

        (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", owner.Token)).Single(b => b.Id == budget.Id).SpentCents.Should().Be(0);
    }

    [Fact]
    public async Task InvitationTokensAndSharedAmountsDoNotReachTheLogs()
    {
        const long distinctiveAmount = 73_519_004L;
        var (owner, groupId) = await NewGroupAsync();
        var member = await NewUserAsync();
        var invite = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", owner.Token, new { role = "Member" });
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", member.Token, new { token = invite.Token });
        await ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { limitCents = 100_000L });
        await SpendAsync(member, distinctiveAmount % 40_000_000, groupId: groupId);

        var logs = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.Exists(logs).Should().BeTrue();
        foreach (var file in Directory.GetFiles(logs, "*.json"))
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            text.Should().NotContain(invite.Token!);
            text.Should().NotContain((distinctiveAmount % 40_000_000).ToString());
        }
    }
}
