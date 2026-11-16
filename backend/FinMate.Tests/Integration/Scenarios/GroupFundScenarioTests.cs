using System.Net;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Phase 85, kịch bản đầu-cuối bắt buộc (a)–(i). Nhóm "Phòng trọ" ba người A, B, C (mỗi người một ví VCB 5.000.000); ngân sách nhóm "Điện nước"
/// 1.000.000 theo danh mục nhóm. Các bước nối tiếp nhau như ngoài đời, nên (a)–(h) là một chuỗi; (i) độc lập.
/// </summary>
[Collection("Integration")]
public class GroupFundScenarioTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const long Start = GroupFundApi.InitialBalance;

    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundScenarioTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record BudgetData(Guid Id, long SpentCents, long LimitCents, int PercentUsed);
    private sealed record PersonalBudget(Guid Id, long SpentCents);

    private sealed record Room(
        GroupFundApi.User A, GroupFundApi.User B, GroupFundApi.User C, GroupFundApi.User Viewer, Guid GroupId, Guid FundId, Guid Utilities, Guid Bills, Guid Budget);

    private async Task<Room> RoomAsync()
    {
        var (a, groupId) = await _api.NewGroupAsync("Phòng trọ", "A");
        var b = await _api.JoinAsync(a, groupId, name: "B");
        var c = await _api.JoinAsync(a, groupId, name: "C");
        var viewer = await _api.JoinAsync(a, groupId, "Viewer", "D");
        var utilities = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/categories", a.Token, new { name = "Điện nước" });
        var budget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", a.Token, new { groupCategoryId = utilities.Id, limitCents = 1_000_000L });
        var fund = await _api.CreateFundAsync(a, groupId, "Tiền điện nước");
        var bills = await _api.SystemCategoryAsync(a.Token, "bills");
        return new Room(a, b, c, viewer, groupId, fund.Id, utilities.Id, bills, budget.Id);
    }

    private async Task<string> FlexibleSpendAsync(GroupFundApi.User user)
    {
        var data = JsonDocument.Parse(await (await _api.SendAsync(HttpMethod.Get, "/api/v1/forecast/cashflow", user.Token)).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data");
        return string.Join(";", data.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("flexibleSpendCents").ToString()));
    }

    private async Task<long> GroupBudgetSpentAsync(Room r)
        => (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{r.GroupId}/budgets", r.A.Token)).Single(x => x.Id == r.Budget).SpentCents;

    private Task<GroupFundApi.EntryResult> SpendFromFundAsync(GroupFundApi.User user, Room r, long amount, string note)
        => _api.RecordAsync(user, r.GroupId, r.FundId, new { kind = "Expense", amountCents = amount, groupCategoryId = r.Utilities, categoryId = r.Bills, note, merchantName = "EVN" });

    [Fact]
    public async Task ScenariosAToH_ThePhongTroFund()
    {
        var r = await RoomAsync();
        var bPersonal = await _api.ReadAsync<PersonalBudget>(HttpMethod.Post, "/api/v1/budgets", r.B.Token, new { categoryId = r.Bills, limitCents = 2_000_000L });
        var spendingBefore = new Dictionary<Guid, GroupFundApi.MonthlySummary>();
        foreach (var user in new[] { r.A, r.B, r.C })
        {
            spendingBefore[user.Id] = await _api.MonthlySummaryAsync(user);
        }

        var aFlexibleBefore = await FlexibleSpendAsync(r.A);

        // (a) A, B, C mỗi người góp 500k từ ví VCB → quỹ 1.500.000; ví mỗi người −500k; chi tiêu cá nhân tháng không đổi.
        var aContribution = (await _api.ContributeAsync(r.A, r.GroupId, r.FundId, 500_000)).Entry;
        await _api.ContributeAsync(r.B, r.GroupId, r.FundId, 500_000);
        var afterA = await _api.ContributeAsync(r.C, r.GroupId, r.FundId, 500_000);
        afterA.Fund.BalanceCents.Should().Be(1_500_000, "(a)");
        foreach (var user in new[] { r.A, r.B, r.C })
        {
            (await _api.BalanceAsync(user.AccountId)).Should().Be(Start - 500_000, "(a) ví −500k");
            (await _api.MonthlySummaryAsync(user)).Should().Be(spendingBefore[user.Id], "(a) chi tiêu cá nhân tháng không đổi");
        }

        // (g) Dòng tiền cá nhân của A thấy −500k như tiền ra khỏi tập ví được tính; thói quen chi linh hoạt không đổi.
        var aLinkId = (await _api.PersonalLinkAsync(aContribution.Id))!.Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var data = scope.ServiceProvider.GetRequiredService<IForecastDataRepository>();
            var today = VietnamTime.Today();
            var outflows = await BoundaryTransfers.OutflowsAsync(data, r.A.Id, new HashSet<Guid> { r.A.AccountId }, today.AddDays(-1), today, default);
            outflows.Should().ContainSingle(t => t.Id == aLinkId && t.AmountCents == 500_000 && t.Type == TransactionType.Debit, "(g) tiền ra khỏi tập ví được tính");
            (await data.GetTransactionsAsync(r.A.Id, today.AddDays(-1), today)).Should().NotContain(t => t.Id == aLinkId, "(g) không vào thói quen chi");
        }

        (await FlexibleSpendAsync(r.A)).Should().Be(aFlexibleBefore, "(g) chi linh hoạt không đổi");

        // (b) B ghi chi 800k "Tiền điện" từ quỹ → quỹ 700k; ngân sách nhóm Điện nước 800k (80%); ngân sách và báo cáo cá nhân của B không đổi.
        var bExpense = await SpendFromFundAsync(r.B, r, 800_000, "Tiền điện");
        bExpense.Fund.BalanceCents.Should().Be(700_000, "(b)");
        var budget = (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{r.GroupId}/budgets", r.A.Token)).Single(x => x.Id == r.Budget);
        budget.SpentCents.Should().Be(800_000, "(b)");
        budget.PercentUsed.Should().Be(80, "(b)");
        (await _api.MonthlySummaryAsync(r.B)).Should().Be(spendingBefore[r.B.Id], "(b) báo cáo cá nhân của B không đổi");
        (await PersonalSpentAsync(bPersonal.Id)).Should().Be(0, "(b) ngân sách cá nhân của B không đổi");

        // (c) C ghi chi 900k → quỹ −200k, cờ âm, push cho người có ManageFunds một lần.
        var cExpense = await SpendFromFundAsync(r.C, r, 900_000, "Tiền nước");
        cExpense.Fund.BalanceCents.Should().Be(-200_000, "(c)");
        cExpense.Fund.IsNegative.Should().BeTrue("(c)");
        var negativeAlerts = await _api.WithDbAsync(db => db.ForecastAlerts.AsNoTracking()
            .Where(x => x.Kind == ForecastAlertKind.GroupFundNegative && x.DedupKey.StartsWith("group_fund_negative:" + r.FundId)).Select(x => x.UserId).ToListAsync());
        negativeAlerts.Should().Equal([r.A.Id], "(c) chỉ A (Owner) có ManageFunds, một lần");

        // (e) Viewer ghi dòng → 403. Member sửa số tiền dòng của người khác → 403.
        await _api.ExpectAsync(HttpMethod.Post, GroupFundApi.Entries(r.GroupId, r.FundId), r.Viewer.Token, HttpStatusCode.Forbidden, new { kind = "Contribution", amountCents = 1L });
        await _api.ExpectAsync(HttpMethod.Patch, $"{GroupFundApi.Entries(r.GroupId, r.FundId)}/{bExpense.Entry.Id}", r.C.Token, HttpStatusCode.Forbidden, new { amountCents = 1L });

        // (d) A rời nhóm (chủ nhóm chuyển chủ cho B trước) → dòng góp của A còn, vẫn ghi tên A; số dư quỹ không đổi; A gọi API quỹ → 404;
        //     giao dịch −500k vẫn trong sổ cá nhân của A.
        (await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{r.GroupId}/transfer-ownership", r.A.Token, new { newOwnerUserId = r.B.Id })).EnsureSuccessStatusCode();
        (await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{r.GroupId}/leave", r.A.Token)).EnsureSuccessStatusCode();
        var lines = await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, GroupFundApi.Entries(r.GroupId, r.FundId), r.B.Token);
        lines.Should().ContainSingle(l => l.Id == aContribution.Id && l.MemberName == "A", "(d) dòng góp của A còn, vẫn ghi tên");
        (await _api.GetFundAsync(r.B, r.GroupId, r.FundId)).BalanceCents.Should().Be(-200_000, "(d) số dư quỹ không đổi");
        await _api.ExpectAsync(HttpMethod.Get, GroupFundApi.Funds(r.GroupId), r.A.Token, HttpStatusCode.NotFound);
        await _api.ExpectAsync(HttpMethod.Get, GroupFundApi.Entries(r.GroupId, r.FundId), r.A.Token, HttpStatusCode.NotFound);
        (await _api.PersonalLinkAsync(aContribution.Id))!.Id.Should().Be(aLinkId, "(d) −500k vẫn trong sổ cá nhân của A");
        (await _api.BalanceAsync(r.A.AccountId)).Should().Be(Start - 500_000);

        // (f) Lưu trữ quỹ còn −200k → 422; góp thêm 200k → lưu trữ được.
        var archive = $"{GroupFundApi.Funds(r.GroupId)}/{r.FundId}/archive";
        await _api.ExpectAsync(HttpMethod.Post, archive, r.B.Token, HttpStatusCode.UnprocessableEntity, code: "GROUP_FUND_NOT_SETTLED");
        await _api.ContributeAsync(r.B, r.GroupId, r.FundId, 200_000);
        (await _api.ReadAsync<GroupFundApi.FundData>(HttpMethod.Post, archive, r.B.Token)).IsArchived.Should().BeTrue("(f)");

        // (h) Xoá cứng tài khoản B → dòng của B thành "Thành viên cũ", số dư quỹ và ngân sách nhóm không đổi.
        var budgetBefore = (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{r.GroupId}/budgets", r.B.Token)).Single(x => x.Id == r.Budget).SpentCents;
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(r.B.Id);
        }

        var survivor = r.C;   // B là chủ nhóm; xoá cứng nâng người kế tiếp lên chủ
        var afterH = await _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, GroupFundApi.Entries(r.GroupId, r.FundId), survivor.Token);
        afterH.Where(l => l.MemberUserId == null).Should().HaveCount(3, "(h) góp 500k, chi 800k, góp 200k của B").And.OnlyContain(l => l.MemberName == "Thành viên cũ");
        afterH.Should().Contain(l => l.Id == bExpense.Entry.Id && l.AmountCents == 800_000, "(h) không xoá");
        (await _api.GetFundAsync(survivor, r.GroupId, r.FundId)).BalanceCents.Should().Be(0, "(h) số dư quỹ không đổi");
        (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{r.GroupId}/budgets", survivor.Token)).Single(x => x.Id == r.Budget).SpentCents
            .Should().Be(budgetBefore, "(h) ngân sách nhóm không đổi").And.Be(1_700_000);
    }

    [Fact]
    public async Task ScenarioI_BTurnsTheBankDebitForElectricityIntoAFundExpense()
    {
        var r = await RoomAsync();
        var before = await _api.MonthlySummaryAsync(r.B);
        var draftId = await _api.WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = r.B.Id, FinancialAccountId = r.B.AccountId, CategoryId = r.Bills, AmountCents = 800_000, TransactionType = TransactionType.Debit,
                Source = TransactionSource.Notification, Status = TransactionStatus.Draft, MerchantName = "EVN", TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });
        await _api.ReadAsync<object>(HttpMethod.Post, $"/api/v1/transactions/{draftId}/confirm", r.B.Token, new { categoryId = r.Bills });

        var result = await _api.RecordAsync(r.B, r.GroupId, r.FundId, new { kind = "Expense", fromTransactionId = draftId, groupCategoryId = r.Utilities });

        (await _api.MonthlySummaryAsync(r.B)).Should().Be(before, "(i) chi tiêu cá nhân của B như trước khi có khoản này");
        result.Fund.BalanceCents.Should().Be(-800_000, "(i) quỹ −800k");
        (await GroupBudgetSpentAsync(r)).Should().Be(800_000, "(i) ngân sách nhóm +800k");
        (await _api.BalanceAsync(r.B.AccountId)).Should().Be(Start - 800_000, "(i) VCB của B vẫn −800k");
    }

    private Task<long> PersonalSpentAsync(Guid budgetId)
        => _api.WithDbAsync(async db => await db.BudgetPeriods.AsNoTracking().Where(p => p.BudgetId == budgetId).Select(p => (long?)p.SpentCents).SumAsync() ?? 0);
}
