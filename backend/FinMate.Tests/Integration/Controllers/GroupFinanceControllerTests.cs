using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Chia sẻ giao dịch, ngân sách và mục tiêu nhóm qua API thật. Trọng tâm: số của nhóm luôn đúng, và dữ liệu cá nhân không đổi và không lộ.</summary>
[Collection("Integration")]
public class GroupFinanceControllerTests : IClassFixture<AuthApiFactory>
{
    private const long InitialBalance = 50_000_000L;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GroupFinanceControllerTests(AuthApiFactory factory)
    {
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
    private record BudgetData(Guid Id, Guid? CategoryId, Guid? GroupCategoryId, string? CategoryName, long LimitCents, long SpentCents, bool IsOverLimit);
    private record GroupTxData(Guid Id, Guid MemberUserId, string MemberName, long AmountCents, string CategoryName, Guid? GroupCategoryId);
    private record BalanceData(long BalanceCents);
    private record PersonalSummaryItem(long SpentCents);
    private record PersonalSummary(PersonalSummaryItem? TotalBudget, List<PersonalSummaryItem> CategoryBudgets);
    private record GoalData(Guid Id, long SavedCents, string Status);
    private record ContributionData(Guid MemberUserId, long AmountCents);
    private record ErrorBody(bool Success, ErrorData Error);
    private record ErrorData(string Code);
    private record TestUser(Guid Id, string Token, Guid AccountId);

    private async Task<TestUser> NewUserAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Thành viên " + email[..4]));
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Password123!"));
        var token = (await login.Content.ReadFromJsonAsync<Wrapper<AuthData>>(JsonOptions))!.Data.AccessToken;
        var me = await ReadAsync<IdData>(HttpMethod.Get, "/api/v1/users/me", token);
        var account = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", token, new { accountType = "Cash", accountName = "Ví", initialBalanceCents = InitialBalance });
        return new TestUser(me.Id, token, account.Id);
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

    private async Task ExpectAsync(HttpMethod method, string url, string token, HttpStatusCode status, object? body = null, string? code = null)
    {
        var response = await SendAsync(method, url, token, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, text);
        if (code is not null)
        {
            JsonSerializer.Deserialize<ErrorBody>(text, JsonOptions)!.Error.Code.Should().Be(code);
        }
    }

    private async Task<(TestUser Owner, Guid GroupId)> NewGroupAsync()
    {
        var owner = await NewUserAsync();
        var group = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Gia đình" });
        return (owner, group.Id);
    }

    private async Task<TestUser> JoinAsync(TestUser owner, Guid groupId, string role = "Member")
    {
        var user = await NewUserAsync();
        var invite = await ReadAsync<InvitationData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/invitations", owner.Token, new { role });
        await ReadAsync<object>(HttpMethod.Post, "/api/v1/group-invitations/accept", user.Token, new { token = invite.Token });
        return user;
    }

    private async Task<Guid> SystemCategoryAsync(string token, string slug)
        => (await ReadAsync<List<CategoryData>>(HttpMethod.Get, "/api/v1/categories", token)).First(c => c.IsSystem && c.Slug == slug).Id;

    private Task<IdData> SpendAsync(TestUser user, long amount, Guid? categoryId = null, Guid? groupId = null, Guid? groupCategoryId = null, DateTimeOffset? at = null)
        => ReadAsync<IdData>(HttpMethod.Post, "/api/v1/transactions", user.Token, new
        {
            financialAccountId = user.AccountId,
            categoryId,
            amountCents = amount,
            transactionType = "Debit",
            transactedAt = at ?? DateTimeOffset.UtcNow,
            merchantName = "Cửa hàng",
            groupId,
            groupCategoryId,
        });

    private Task<BudgetData> NewBudgetAsync(TestUser manager, Guid groupId, long limit = 100_000_000L, Guid? categoryId = null, Guid? groupCategoryId = null)
        => ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", manager.Token, new { categoryId, groupCategoryId, limitCents = limit });

    private async Task<long> SpentAsync(TestUser viewer, Guid groupId, Guid budgetId)
        => (await ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", viewer.Token)).Single(b => b.Id == budgetId).SpentCents;

    private async Task<long> BalanceAsync(TestUser user)
        => (await ReadAsync<BalanceData>(HttpMethod.Get, $"/api/v1/financial-accounts/{user.AccountId}/balance", user.Token)).BalanceCents;

    [Fact]
    public async Task SharingChargesTheGroupBudget_AndTheWalletAndPersonalBudgetBehaveExactlyAsBefore()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var budget = await NewBudgetAsync(owner, groupId);
        await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", member.Token, new { categoryId = (Guid?)null, limitCents = 90_000_000L });

        await SpendAsync(member, 300_000, groupId: groupId);
        await SpendAsync(member, 50_000);   // không chia sẻ

        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(300_000);
        (await BalanceAsync(member)).Should().Be(InitialBalance - 350_000);
        var personal = await ReadAsync<PersonalSummary>(HttpMethod.Get, "/api/v1/budgets", member.Token);
        personal.TotalBudget!.SpentCents.Should().Be(350_000);   // quyết định: khoản chia sẻ vẫn tính vào ngân sách cá nhân
    }

    [Fact]
    public async Task EditingDeletingAndMovingASharedTransactionKeepTheBudgetExact()
    {
        var (owner, groupId) = await NewGroupAsync();
        var otherGroup = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/groups", owner.Token, new { name = "Bạn bè" });
        var budget = await NewBudgetAsync(owner, groupId);
        var otherBudget = await NewBudgetAsync(owner, otherGroup.Id);
        var tx = await SpendAsync(owner, 100_000, groupId: groupId);

        await ReadAsync<IdData>(HttpMethod.Patch, $"/api/v1/transactions/{tx.Id}", owner.Token, new
        {
            financialAccountId = owner.AccountId,
            amountCents = 250_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
        });
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(250_000);

        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{tx.Id}/group", owner.Token, HttpStatusCode.NoContent, new { groupId = otherGroup.Id });
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(0);
        (await SpentAsync(owner, otherGroup.Id, otherBudget.Id)).Should().Be(250_000);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{tx.Id}/group", owner.Token, HttpStatusCode.NoContent);
        (await SpentAsync(owner, otherGroup.Id, otherBudget.Id)).Should().Be(0);

        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{tx.Id}/group", owner.Token, HttpStatusCode.NoContent, new { groupId });
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(250_000);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{tx.Id}", owner.Token, HttpStatusCode.NoContent);
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(0);
    }

    [Fact]
    public async Task ADraftCountsOnlyOnceConfirmed()
    {
        var (owner, groupId) = await NewGroupAsync();
        var budget = await NewBudgetAsync(owner, groupId);
        Guid draftId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var draft = new FinMate.Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                UserId = owner.Id,
                FinancialAccountId = owner.AccountId,
                AmountCents = 70_000,
                TransactionType = TransactionType.Debit,
                Source = TransactionSource.Notification,
                Status = TransactionStatus.Draft,
                TransactedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            draftId = draft.Id;
        }

        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{draftId}/group", owner.Token, (HttpStatusCode)422, new { groupId }, "GROUP_TRANSACTION_NOT_SHAREABLE");

        await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/transactions/{draftId}/confirm", owner.Token, new { groupId });
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(70_000);
    }

    [Fact]
    public async Task TransfersLoansAndOutsidersCannotShare()
    {
        var (owner, groupId) = await NewGroupAsync();
        var stranger = await NewUserAsync();
        var viewer = await JoinAsync(owner, groupId, "Viewer");
        var loan = await SystemCategoryAsync(owner.Token, "loan");
        var second = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/financial-accounts", owner.Token, new { accountType = "Cash", accountName = "Ví 2", initialBalanceCents = 0L });
        var transfer = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/transactions/transfer", owner.Token, new { fromAccountId = owner.AccountId, toAccountId = second.Id, amountCents = 10_000L, transactedAt = DateTimeOffset.UtcNow });
        var loanTx = await SpendAsync(owner, 10_000, categoryId: loan);
        var ordinary = await SpendAsync(stranger, 10_000);
        var viewerTx = await SpendAsync(viewer, 10_000);

        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{transfer.Id}/group", owner.Token, (HttpStatusCode)422, new { groupId }, "GROUP_TRANSACTION_NOT_SHAREABLE");
        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{loanTx.Id}/group", owner.Token, (HttpStatusCode)422, new { groupId }, "GROUP_TRANSACTION_NOT_SHAREABLE");
        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{ordinary.Id}/group", stranger.Token, HttpStatusCode.NotFound, new { groupId });
        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{viewerTx.Id}/group", viewer.Token, HttpStatusCode.Forbidden, new { groupId });
        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{ordinary.Id}/group", owner.Token, HttpStatusCode.NotFound, new { groupId });   // giao dịch của người khác
    }

    [Fact]
    public async Task TheGroupViewNeverExposesWalletOrBalanceFields()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        await SpendAsync(member, 40_000, groupId: groupId);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fields = JsonDocument.Parse(body).RootElement.GetProperty("data")[0].EnumerateObject().Select(p => p.Name).ToList();
        fields.Should().NotContain(["financialAccountId", "balanceAfterCents", "notificationLogId", "clientRequestId", "source"]);
    }

    [Fact]
    public async Task UnsharedTransactionsAndOtherGroupsStayInvisible()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var stranger = await NewUserAsync();
        await SpendAsync(member, 11_000);
        var shared = await SpendAsync(member, 22_000, groupId: groupId);

        (await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token)).Should().ContainSingle().Which.Id.Should().Be(shared.Id);
        await ExpectAsync(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", stranger.Token, HttpStatusCode.NotFound);
        await ExpectAsync(HttpMethod.Get, $"/api/v1/transactions/{shared.Id}", owner.Token, HttpStatusCode.NotFound);   // đường cá nhân vẫn chỉ của chủ
    }

    [Fact]
    public async Task SummaryVisibilityLeavesMembersOnlyTheirOwnDetails_ButTotalsStayVisible()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var budget = await NewBudgetAsync(owner, groupId);
        await SpendAsync(owner, 30_000, groupId: groupId);
        await SpendAsync(member, 20_000, groupId: groupId);

        (await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", member.Token)).Should().HaveCount(2);

        await ReadAsync<object>(HttpMethod.Patch, $"/api/v1/groups/{groupId}", owner.Token, new { transactionVisibility = "Summary" });

        (await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", member.Token)).Should().ContainSingle().Which.MemberUserId.Should().Be(member.Id);
        (await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token)).Should().HaveCount(2);
        (await SpentAsync(member, groupId, budget.Id)).Should().Be(50_000);
    }

    [Fact]
    public async Task APersonalCustomCategoryIsNotRevealed_AndCountsOnlyInTheTotalBudget()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var food = await SystemCategoryAsync(owner.Token, "food");
        var total = await NewBudgetAsync(owner, groupId);
        var foodBudget = await NewBudgetAsync(owner, groupId, categoryId: food);
        var privateCategory = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/categories", member.Token, new { name = "Việc riêng tư" });

        await SpendAsync(member, 15_000, categoryId: privateCategory.Id, groupId: groupId);
        await SpendAsync(member, 25_000, categoryId: await SystemCategoryAsync(member.Token, "food"), groupId: groupId);

        (await SpentAsync(owner, groupId, total.Id)).Should().Be(40_000);
        (await SpentAsync(owner, groupId, foodBudget.Id)).Should().Be(25_000);
        var rows = await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token);
        rows.Select(r => r.CategoryName).Should().NotContain("Việc riêng tư").And.Contain("Chưa phân loại");
    }

    [Fact]
    public async Task AManagerReclassifiesInTheGroupWithoutTouchingThePersonalRecord_AndCannotEditMoney()
    {
        var (owner, groupId) = await NewGroupAsync();
        var manager = await JoinAsync(owner, groupId, "Manager");
        var member = await JoinAsync(owner, groupId);
        var food = await SystemCategoryAsync(member.Token, "food");
        var groceries = await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/categories", manager.Token, new { name = "Đi chợ" });
        var groceriesBudget = await NewBudgetAsync(manager, groupId, groupCategoryId: groceries.Id);
        var foodBudget = await NewBudgetAsync(manager, groupId, categoryId: food);
        var tx = await SpendAsync(member, 60_000, categoryId: food, groupId: groupId);
        (await SpentAsync(owner, groupId, foodBudget.Id)).Should().Be(60_000);

        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{groupId}/transactions/{tx.Id}", member.Token, HttpStatusCode.Forbidden, new { groupCategoryId = groceries.Id });
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{groupId}/transactions/{tx.Id}", manager.Token, HttpStatusCode.NoContent, new { groupCategoryId = groceries.Id });

        (await SpentAsync(owner, groupId, foodBudget.Id)).Should().Be(0);
        (await SpentAsync(owner, groupId, groceriesBudget.Id)).Should().Be(60_000);
        var personal = await ReadAsync<JsonElement>(HttpMethod.Get, $"/api/v1/transactions/{tx.Id}", member.Token);
        personal.GetProperty("categoryId").GetGuid().Should().Be(food);

        await ExpectAsync(HttpMethod.Patch, $"/api/v1/transactions/{tx.Id}", manager.Token, HttpStatusCode.NotFound, new { financialAccountId = member.AccountId, amountCents = 1L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow });
        await ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{tx.Id}", manager.Token, HttpStatusCode.NotFound);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}/transactions/{tx.Id}", manager.Token, HttpStatusCode.NoContent);
        (await SpentAsync(owner, groupId, groceriesBudget.Id)).Should().Be(0);
        (await BalanceAsync(member)).Should().Be(InitialBalance - 60_000);
    }

    [Fact]
    public async Task AGroupCategoryInUseCannotBeDeleted_AndBudgetPermissionsAreEnforced()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var category = await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/categories", owner.Token, new { name = "Con cái" });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/categories", owner.Token, HttpStatusCode.Conflict, new { name = "Con cái" }, "GROUP_CATEGORY_DUPLICATE");
        var budget = await NewBudgetAsync(owner, groupId, groupCategoryId: category.Id);

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}/categories/{category.Id}", owner.Token, HttpStatusCode.Conflict, code: "GROUP_CATEGORY_IN_USE");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", member.Token, HttpStatusCode.Forbidden, new { limitCents = 1000L });
        await ExpectAsync(HttpMethod.Patch, $"/api/v1/groups/{groupId}/budgets/{budget.Id}", member.Token, HttpStatusCode.Forbidden, new { limitCents = 1000L });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, HttpStatusCode.Conflict, new { groupCategoryId = category.Id, limitCents = 1000L }, "GROUP_BUDGET_ALREADY_EXISTS");

        var personalCategory = await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/categories", owner.Token, new { name = "Của riêng" });
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, (HttpStatusCode)422, new { categoryId = personalCategory.Id, limitCents = 1000L }, "GROUP_BUDGET_INVALID_CATEGORY");
    }

    [Fact]
    public async Task ABudgetCreatedMidMonthPicksUpWhatWasAlreadyShared()
    {
        var (owner, groupId) = await NewGroupAsync();
        var food = await SystemCategoryAsync(owner.Token, "food");
        await SpendAsync(owner, 80_000, categoryId: food, groupId: groupId);
        await SpendAsync(owner, 5_000, groupId: groupId);
        await SpendAsync(owner, 9_000, categoryId: food);   // không chia sẻ

        (await NewBudgetAsync(owner, groupId)).SpentCents.Should().Be(85_000);
        (await NewBudgetAsync(owner, groupId, categoryId: food)).SpentCents.Should().Be(80_000);
    }

    [Fact]
    public async Task GroupBudgetsAndGoalsNeverAppearInPersonalLists()
    {
        var (owner, groupId) = await NewGroupAsync();
        await NewBudgetAsync(owner, groupId);
        await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", owner.Token, new { name = "Du lịch", targetCents = 1_000_000L });

        var personal = await ReadAsync<PersonalSummary>(HttpMethod.Get, "/api/v1/budgets", owner.Token);
        personal.TotalBudget.Should().BeNull();
        personal.CategoryBudgets.Should().BeEmpty();
        (await ReadAsync<List<GoalData>>(HttpMethod.Get, "/api/v1/saving-goals", owner.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task ManyMembersSpendingAtOnceLoseNothing()
    {
        var (owner, groupId) = await NewGroupAsync();
        var budget = await NewBudgetAsync(owner, groupId);
        var members = new List<TestUser> { owner };
        for (var i = 0; i < 5; i++)
        {
            members.Add(await JoinAsync(owner, groupId));
        }

        var responses = await Task.WhenAll(members.Select(m => SendAsync(HttpMethod.Post, "/api/v1/transactions", m.Token, new
        {
            financialAccountId = m.AccountId,
            amountCents = 10_000L,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            groupId,
        })));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        succeeded.Should().BeGreaterThan(0);
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(succeeded * 10_000L);
        (await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token)).Should().HaveCount(succeeded);
    }

    [Fact]
    public async Task ARuleAutoSharesMatchingSpending_OnlyForItsOwner_AndStopsWhenTheyLeave()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var food = await SystemCategoryAsync(member.Token, "food");
        var budget = await NewBudgetAsync(owner, groupId);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/share-rules", member.Token, HttpStatusCode.NotFound, new { financialAccountId = owner.AccountId });
        await ReadAsync<IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/share-rules", member.Token, new { categoryId = food });

        await SpendAsync(member, 30_000, categoryId: food);
        await SpendAsync(member, 7_000);          // danh mục khác
        await SpendAsync(owner, 9_000, categoryId: food);   // người khác
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(30_000);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", member.Token, HttpStatusCode.NoContent);
        await SpendAsync(member, 44_000, categoryId: food);   // vẫn lưu được, chỉ không vào nhóm
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(30_000);
    }

    [Fact]
    public async Task AfterLeaving_HistoryKeepsTheName_EditsStillApply_ButSharingAndUnsharingStop()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var budget = await NewBudgetAsync(owner, groupId);
        var kept = await SpendAsync(member, 40_000, groupId: groupId);
        var deleted = await SpendAsync(member, 10_000, groupId: groupId);
        var later = await SpendAsync(member, 1_000);

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", member.Token, HttpStatusCode.NoContent);

        var rows = await ReadAsync<List<GroupTxData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.MemberName.StartsWith("Thành viên "));
        rows.Should().OnlyContain(r => r.MemberName != "Thành viên không còn hoạt động");

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{kept.Id}/group", member.Token, HttpStatusCode.NotFound);
        await ExpectAsync(HttpMethod.Put, $"/api/v1/transactions/{later.Id}/group", member.Token, HttpStatusCode.NotFound, new { groupId });

        await ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{deleted.Id}", member.Token, HttpStatusCode.NoContent);
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(40_000);
    }

    [Fact]
    public async Task CrossingAThresholdAlertsEachMemberOnce_AndRespectsTheirSwitches()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var muted = await JoinAsync(owner, groupId);
        var viewer = await JoinAsync(owner, groupId, "Viewer");
        await ExpectAsync(HttpMethod.Put, $"/api/v1/groups/{groupId}/alerts", muted.Token, HttpStatusCode.NoContent, new { enabled = false });
        var budget = await NewBudgetAsync(owner, groupId, limit: 100_000);

        await SpendAsync(member, 75_000, groupId: groupId);
        await SpendAsync(member, 1_000, groupId: groupId);   // vẫn trong mức T1: không báo thêm

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var alerts = await db.ForecastAlerts.Where(a => a.Kind == ForecastAlertKind.GroupBudget && a.DedupKey.StartsWith(budget.Id.ToString())).ToListAsync();
        alerts.Select(a => a.UserId).Should().BeEquivalentTo([owner.Id, member.Id, viewer.Id]);
        alerts.Should().OnlyContain(a => !a.Body.Contains("75") && !a.Data.Contains("amount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheHourlySweepAlertsForAGroupBudgetRaisedOutsideATransaction()
    {
        var (owner, groupId) = await NewGroupAsync();
        await SpendAsync(owner, 80_000, groupId: groupId);
        var budget = await NewBudgetAsync(owner, groupId, limit: 100_000);   // tạo sau khi đã chi: không có giao dịch nào kích hoạt cảnh báo

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BudgetAlertJob>().RunAsync();
        }

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.ForecastAlerts.CountAsync(a => a.UserId == owner.Id && a.Kind == ForecastAlertKind.GroupBudget && a.DedupKey.StartsWith(budget.Id.ToString()))).Should().Be(1);
    }

    [Fact]
    public async Task GoalContributionsAreBookkeeping_RespectPermissions_AndCompletionAlertsMembers()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var viewer = await JoinAsync(owner, groupId, "Viewer");
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", member.Token, HttpStatusCode.Forbidden, new { name = "X", targetCents = 1000L });
        var goal = await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", owner.Token, new { name = "Du lịch", targetCents = 100_000L });

        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", viewer.Token, HttpStatusCode.Forbidden, new { amountCents = 1000L });
        await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", member.Token, new { amountCents = 40_000L });
        var done = await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", owner.Token, new { amountCents = 60_000L });

        done.Status.Should().BeEquivalentTo("Completed");
        (await BalanceAsync(member)).Should().Be(InitialBalance);
        (await ReadAsync<List<ContributionData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contributions", viewer.Token)).Should().HaveCount(2);
        await ExpectAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals/{goal.Id}/contribute", member.Token, (HttpStatusCode)422, new { amountCents = 1L }, "GROUP_GOAL_NOT_ACTIVE");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.ForecastAlerts.Where(a => a.Kind == ForecastAlertKind.GroupGoal && a.DedupKey.StartsWith(goal.Id.ToString())).Select(a => a.UserId).ToListAsync())
            .Should().BeEquivalentTo([owner.Id, member.Id, viewer.Id]);
    }

    [Fact]
    public async Task TheDeadlineJobRemindsEveryMemberOfAnOverdueGroupGoalOnce()
    {
        var (owner, groupId) = await NewGroupAsync();
        var member = await JoinAsync(owner, groupId);
        var goal = await ReadAsync<GoalData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/goals", owner.Token, new { name = "Quá hạn", targetCents = 100_000L, deadline = DateTimeOffset.UtcNow.AddDays(-1) });

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GoalDeadlineCheckJob>().RunAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<GoalDeadlineCheckJob>().RunAsync();
        }

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.ForecastAlerts.Where(a => a.Kind == ForecastAlertKind.GroupGoal && a.DedupKey == $"{goal.Id}:overdue").Select(a => a.UserId).ToListAsync())
            .Should().BeEquivalentTo([owner.Id, member.Id]);
    }

    /// <summary>Lỗi có sẵn được sửa cùng đợt này: hoàn rồi áp trong cùng một lần lưu từng tạo hai kỳ trùng khóa.</summary>
    [Fact]
    public async Task EditingATransactionFromAMonthWithNoPeriodWorks()
    {
        var owner = await NewUserAsync();
        var lastMonth = DateTimeOffset.UtcNow.AddMonths(-1);
        var tx = await SpendAsync(owner, 10_000, at: lastMonth);
        await ReadAsync<IdData>(HttpMethod.Post, "/api/v1/budgets", owner.Token, new { categoryId = (Guid?)null, limitCents = 100_000L });

        var response = await SendAsync(HttpMethod.Patch, $"/api/v1/transactions/{tx.Id}", owner.Token, new
        {
            financialAccountId = owner.AccountId,
            amountCents = 20_000L,
            transactionType = "Debit",
            transactedAt = lastMonth,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
