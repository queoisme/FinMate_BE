using System.Net;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, BR-FUND-13..15, D12: dòng quỹ thuộc về nhóm. Rời hoặc bị xoá khỏi nhóm thì dòng ở lại, vẫn ghi tên; chờ xoá tài khoản thì hiện tên thay thế;
/// xoá cứng thì dòng thành "Thành viên cũ" mà số dư quỹ, ngân sách nhóm và báo cáo tháng không đổi.
/// </summary>
[Collection("Integration")]
public class GroupFundMembershipTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundMembershipTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record BudgetData(Guid Id, long SpentCents);
    private sealed record Former(long TotalSpentCents, long FundSpentCents);
    private sealed record MemberTotals(Guid UserId, long SpentCents);
    private sealed record Monthly(long TotalSpentCents, long FundSpentCents, List<MemberTotals> ByMember, Former? FormerMembers);

    private Task<List<GroupFundApi.EntryData>> EntriesAsync(GroupFundApi.User viewer, Guid groupId, Guid fundId)
        => _api.ReadAsync<List<GroupFundApi.EntryData>>(HttpMethod.Get, GroupFundApi.Entries(groupId, fundId), viewer.Token);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LeavingOrBeingRemoved_KeepsTheLinesUnderTheirName_AndLocksThemOut(bool leaves)
    {
        var (owner, groupId) = await _api.NewGroupAsync(ownerName: "Chủ");
        var a = await _api.JoinAsync(owner, groupId, name: "A");
        var fund = await _api.CreateFundAsync(owner, groupId);
        var entry = (await _api.ContributeAsync(a, groupId, fund.Id, 500_000)).Entry;
        var linkId = (await _api.PersonalLinkAsync(entry.Id))!.Id;

        var gone = leaves
            ? await _api.SendAsync(HttpMethod.Post, $"/api/v1/groups/{groupId}/leave", a.Token)
            : await _api.SendAsync(HttpMethod.Delete, $"/api/v1/groups/{groupId}/members/{a.Id}", owner.Token);
        gone.EnsureSuccessStatusCode();

        var lines = await EntriesAsync(owner, groupId, fund.Id);
        lines.Should().ContainSingle(l => l.Id == entry.Id && l.MemberUserId == a.Id && l.MemberName == "A", "BR-FUND-13: dòng ở lại, vẫn ghi tên");
        (await _api.GetFundAsync(owner, groupId, fund.Id)).BalanceCents.Should().Be(500_000);

        var url = GroupFundApi.Entries(groupId, fund.Id);
        await _api.ExpectAsync(HttpMethod.Get, GroupFundApi.Funds(groupId), a.Token, HttpStatusCode.NotFound);
        await _api.ExpectAsync(HttpMethod.Post, url, a.Token, HttpStatusCode.NotFound, new { kind = "Contribution", amountCents = 1L });
        await _api.ExpectAsync(HttpMethod.Patch, $"{url}/{entry.Id}", a.Token, HttpStatusCode.NotFound, new { note = "x" });
        await _api.ExpectAsync(HttpMethod.Delete, $"{url}/{entry.Id}", a.Token, HttpStatusCode.NotFound);

        (await _api.PersonalLinkAsync(entry.Id))!.Id.Should().Be(linkId, "giao dịch cá nhân vẫn ở sổ của họ");
        (await _api.BalanceAsync(a.AccountId)).Should().Be(GroupFundApi.InitialBalance - 500_000);
    }

    [Fact]
    public async Task AMemberAwaitingDeletion_ShowsUnderTheReplacementName()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var a = await _api.JoinAsync(owner, groupId, name: "A");
        var fund = await _api.CreateFundAsync(owner, groupId);
        await _api.ContributeAsync(a, groupId, fund.Id, 500_000, fromWallet: false);
        await _api.WithDbAsync(db => db.Users.Where(u => u.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow)));

        var lines = await EntriesAsync(owner, groupId, fund.Id);

        lines.Single().MemberName.Should().Be("Thành viên không còn hoạt động", "BR-FUND-14: như nơi khác trong nhóm");
        lines.Single().MemberUserId.Should().Be(a.Id);
    }

    [Fact]
    public async Task HardDeletingAMember_AnonymisesTheirLines_AndKeepsEveryGroupNumber()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var b = await _api.JoinAsync(owner, groupId, name: "B");
        var fund = await _api.CreateFundAsync(owner, groupId);
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        var budget = await _api.ReadAsync<BudgetData>(HttpMethod.Post, $"/api/v1/groups/{groupId}/budgets", owner.Token, new { categoryId = bills, limitCents = 1_000_000L });
        await _api.ContributeAsync(owner, groupId, fund.Id, 1_000_000, fromWallet: false);
        await _api.ContributeAsync(b, groupId, fund.Id, 500_000);
        await _api.SpendAsync(b, groupId, fund.Id, 800_000, bills);

        // B cũng có một khoản chia sẻ cùng danh mục trong kỳ: xoá cứng tính lại kỳ này, và phải giữ phần chi từ quỹ (D13).
        await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", b.Token, new
        {
            financialAccountId = b.AccountId, categoryId = bills, amountCents = 100_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, groupId,
        });
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(900_000);

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(b.Id);
        }

        var lines = await EntriesAsync(owner, groupId, fund.Id);
        lines.Where(l => l.MemberUserId == null).Should().HaveCount(2).And.OnlyContain(l => l.MemberName == "Thành viên cũ", "BR-FUND-14: ẩn danh, không xoá");
        (await _api.GetFundAsync(owner, groupId, fund.Id)).BalanceCents.Should().Be(1_000_000 + 500_000 - 800_000, "số dư quỹ không đổi");
        (await SpentAsync(owner, groupId, budget.Id)).Should().Be(800_000, "khoản chia sẻ của B mất theo B (FR-GRP-16), khoản chi từ quỹ ở lại");

        var report = await _api.ReadAsync<Monthly>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", owner.Token);
        report.TotalSpentCents.Should().Be(800_000);
        report.FormerMembers.Should().Be(new Former(800_000, 800_000), "S5: phần của người đã ẩn danh");
        report.ByMember.Should().NotContain(m => m.UserId == b.Id);
    }

    private async Task<long> SpentAsync(GroupFundApi.User viewer, Guid groupId, Guid budgetId)
        => (await _api.ReadAsync<List<BudgetData>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/budgets", viewer.Token)).Single(x => x.Id == budgetId).SpentCents;
}
