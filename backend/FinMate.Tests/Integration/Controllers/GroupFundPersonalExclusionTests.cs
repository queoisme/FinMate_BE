using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 85, BR-FUND-08/11, Task 11–12: giao dịch cá nhân gắn quỹ (góp, rút, chi từ ví) đổi số dư ví nhưng KHÔNG là chi, không là thu ở bất cứ đâu:
/// báo cáo tháng, theo danh mục, theo ngày, ngân sách cá nhân, hũ, kế hoạch tháng (<c>/me/spendable</c>, cũng là số Agent đọc), dự báo theo danh mục,
/// dự báo thu nhập, bất thường và chi linh hoạt hằng ngày của dòng tiền. Chỉ số dư đầu của dòng tiền đổi đúng bằng tổng vế của chúng.
/// </summary>
[Collection("Integration")]
public class GroupFundPersonalExclusionTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly GroupFundApi _api;

    public GroupFundPersonalExclusionTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new GroupFundApi(factory);
    }

    public Task InitializeAsync() => _factory.SetGroupFundsEnabledAsync(true);

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> DataAsync(GroupFundApi.User user, string url)
    {
        var response = await _api.SendAsync(HttpMethod.Get, url, user.Token);
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"{url}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    /// <summary>Ảnh chụp mọi con số cá nhân có thể bị một khoản chi/thu làm đổi, trừ số dư ví.</summary>
    private async Task<Dictionary<string, string>> SnapshotAsync(GroupFundApi.User user)
    {
        var monthly = await DataAsync(user, "/api/v1/reports/monthly-summary");
        var breakdown = await DataAsync(user, "/api/v1/reports/category-breakdown");
        var timeline = await DataAsync(user, "/api/v1/reports/timeline");
        var budgets = await DataAsync(user, "/api/v1/budgets");
        var spendable = await DataAsync(user, "/api/v1/me/spendable");
        var jars = await DataAsync(user, "/api/v1/me/jars");
        var categories = await DataAsync(user, "/api/v1/forecast/categories");
        var income = await DataAsync(user, "/api/v1/forecast/income");
        var anomalies = await DataAsync(user, "/api/v1/forecast/anomalies");
        var cashflow = await DataAsync(user, "/api/v1/forecast/cashflow");

        return new Dictionary<string, string>
        {
            ["monthly.spent"] = monthly.GetProperty("totalSpentCents").ToString(),
            ["monthly.income"] = monthly.GetProperty("totalIncomeCents").ToString(),
            // transactionCount cố ý không so: transfer (kể cả gắn quỹ) vẫn được đếm như trước, đếm không phải chi hay thu.
            ["breakdown"] = breakdown.GetProperty("items").ToString() + breakdown.GetProperty("totalSpentCents"),
            ["timeline.totals"] = string.Join(";", (timeline.ValueKind == JsonValueKind.Array ? timeline : timeline.GetProperty("days")).EnumerateArray()
                .Select(d => $"{d.GetProperty("date")}:{d.GetProperty("totalSpentCents")}:{d.GetProperty("totalIncomeCents")}")),
            ["budgets"] = budgets.ToString(),
            ["spendable.spent"] = spendable.GetProperty("spentCents").ToString(),
            ["spendable.left"] = spendable.GetProperty("spendableCents").ToString(),
            ["jars"] = jars.ToString(),
            ["forecast.categories"] = categories.GetProperty("projectedTotalCents") + "/" + categories.GetProperty("previousMonthTotalCents"),
            ["forecast.income"] = income.ToString(),
            ["anomalies"] = anomalies.ToString(),
            ["cashflow.flexible"] = string.Join(";", cashflow.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("flexibleSpendCents").ToString())),
        };
    }

    [Fact]
    public async Task FundTransfers_MoveTheWalletOnly()
    {
        var (owner, groupId) = await _api.NewGroupAsync();
        var fund = await _api.CreateFundAsync(owner, groupId);
        var food = await _api.SystemCategoryAsync(owner.Token, "food");
        var bills = await _api.SystemCategoryAsync(owner.Token, "bills");
        (await _api.SendAsync(HttpMethod.Patch, "/api/v1/users/me", owner.Token, new { displayName = "A", monthlyIncomeCents = 15_000_000L })).EnsureSuccessStatusCode();
        await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/budgets", owner.Token, new { categoryId = bills, limitCents = 2_000_000L });
        foreach (var amount in new[] { 45_000L, 60_000L, 120_000L })
        {
            await _api.ReadAsync<GroupFundApi.IdData>(HttpMethod.Post, "/api/v1/transactions", owner.Token, new
            {
                financialAccountId = owner.AccountId, categoryId = food, amountCents = amount, transactionType = "Debit", transactedAt = DateTimeOffset.UtcNow, merchantName = "Quán cơm",
            });
        }

        var before = await SnapshotAsync(owner);
        var startBefore = (await DataAsync(owner, "/api/v1/forecast/cashflow")).GetProperty("startBalanceCents").GetInt64();

        await _api.ContributeAsync(owner, groupId, fund.Id, 500_000);
        await _api.RecordAsync(owner, groupId, fund.Id, new { kind = "Withdrawal", amountCents = 200_000L, financialAccountId = owner.AccountId });
        await _api.SpendAsync(owner, groupId, fund.Id, 300_000, bills, accountId: owner.AccountId);

        var after = await SnapshotAsync(owner);
        after.Should().Equal(before);
        var startAfter = (await DataAsync(owner, "/api/v1/forecast/cashflow")).GetProperty("startBalanceCents").GetInt64();
        startAfter.Should().Be(startBefore - 500_000 + 200_000 - 300_000, "số dư đầu của dòng tiền đổi đúng bằng vế của các giao dịch gắn quỹ");
    }
}
