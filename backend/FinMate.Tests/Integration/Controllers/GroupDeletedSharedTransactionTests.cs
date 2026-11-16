using System.Net;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Lỗi có sẵn tìm ra ở Phase 85: <c>IgnoreQueryFilters()</c> trên bảng người dùng tắt bộ lọc xoá mềm của CẢ câu truy vấn, nên giao dịch đã chia sẻ rồi
/// bị xoá (hoặc bị gộp) vẫn vào báo cáo tháng và danh sách giao dịch của nhóm.
/// </summary>
[Collection("Integration")]
public class GroupDeletedSharedTransactionTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupDeletedSharedTransactionTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Report(long TotalSpentCents);
    private sealed record GroupTx(Guid Id);

    [Fact]
    public async Task ADeletedSharedPurchase_LeavesTheGroupReportAndList()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var food = await _api.SystemCategoryAsync(owner.Token, "food");
        var kept = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", owner.Token, new
        {
            financialAccountId = owner.AccountId, categoryId = food, amountCents = 100_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, groupId,
        });
        var deleted = await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", owner.Token, new
        {
            financialAccountId = owner.AccountId, categoryId = food, amountCents = 400_000L, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, groupId,
        });

        await _api.ExpectAsync(HttpMethod.Delete, $"/api/v1/transactions/{deleted.Id}", owner.Token, HttpStatusCode.NoContent);

        (await _api.ReadAsync<Report>(HttpMethod.Get, $"/api/v1/groups/{groupId}/reports/monthly", owner.Token)).TotalSpentCents.Should().Be(100_000);
        (await _api.ReadAsync<List<GroupTx>>(HttpMethod.Get, $"/api/v1/groups/{groupId}/transactions", owner.Token)).Select(t => t.Id).Should().Equal(kept.Id);
    }
}
