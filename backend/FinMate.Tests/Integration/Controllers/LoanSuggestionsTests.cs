using System.Net;
using System.Text.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Gợi ý khoản vay tính khi đọc (Phase 90, Task 8; chép từ bản sổ nợ Phase 86 cũ): BR-DEBT-13, D8, D9, S1, S3, Q15; liên kết hiện trên DTO giao dịch.</summary>
[Collection("Integration")]
public class LoanSuggestionsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public LoanSuggestionsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    private static async Task<Guid> LoanAsync(LoanTestApi api, string name, long principal = 2_000_000, string direction = "lend", decimal rate = 0, DateOnly? start = null)
        => (await api.ExpectAsync(HttpMethod.Post, "/api/v1/loans", new
        {
            direction, title = $"Khoản {name}", counterparty = name, principalCents = principal, interestRatePercentYear = rate, startDate = start,
        }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

    /// <summary>Nháp từ thông báo ngân hàng, kèm kết quả AI có (hoặc không) gợi ý trả nợ.</summary>
    private static Task<Guid> NotificationDraftAsync(LoanTestApi api, Guid wallet, long amount, string text, DebtHintStrength? hint)
        => api.InDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var log = new NotificationLog
            {
                Id = Guid.NewGuid(), UserId = api.UserId, FinancialAccountId = wallet, PackageName = "com.VCB", ContentHash = Guid.NewGuid().ToString("N"),
                ReceivedAt = now, Status = NotificationLogStatus.Processed, CreatedAt = now,
            };
            db.NotificationLogs.Add(log);
            db.AiResults.Add(new AiResult
            {
                Id = Guid.NewGuid(), NotificationLogId = log.Id, PipelineResult = PipelineResult.Financial, CreatedAt = now,
                RelationHint = hint is null ? null : NotificationRelationHint.DebtRepayment, DebtHintStrength = hint,
            });
            var draft = new Transaction
            {
                Id = Guid.NewGuid(), UserId = api.UserId, FinancialAccountId = wallet, AmountCents = amount, TransactionType = TransactionType.Credit,
                Source = TransactionSource.Notification, Status = TransactionStatus.Draft, MerchantName = text, NotificationLogId = log.Id,
                TransactedAt = now, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(draft);
            await db.SaveChangesAsync();
            return draft.Id;
        });

    private static async Task<JsonElement> InListAsync(LoanTestApi api, Guid id)
        => (await api.ExpectAsync(HttpMethod.Get, "/api/v1/transactions?limit=50", null, HttpStatusCode.OK)).EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == id);

    private static bool HasSuggestions(JsonElement dto)
        => dto.TryGetProperty("loanSuggestions", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0;

    [Fact]
    public async Task ADraftNamingTheDebtor_GetsTheDebt_First_WithNameMatched()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        await LoanAsync(api, "Lan", 500_000);
        var nam = await LoanAsync(api, "Nam");
        var draft = await NotificationDraftAsync(api, vcb, 500_000, "NAM TRA NO", DebtHintStrength.Strong);

        var dto = await InListAsync(api, draft);

        var first = dto.GetProperty("loanSuggestions")[0];
        first.GetProperty("loanId").GetGuid().Should().Be(nam);
        first.GetProperty("nameMatched").GetBoolean().Should().BeTrue();
        dto.GetProperty("loanSuggestions").GetArrayLength().Should().Be(2, "gợi ý mạnh vẫn đưa khoản của Lan, xếp sau");
        var detail = await api.TransactionDetailAsync(draft);
        detail.GetProperty("loanSuggestions")[0].GetProperty("loanId").GetGuid().Should().Be(nam);
    }

    [Fact]
    public async Task AWeakHintWithoutAName_SuggestsNothing()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        await LoanAsync(api, "Nam");
        var draft = await NotificationDraftAsync(api, vcb, 200_000, "Shopee tra lai tien don hang", DebtHintStrength.Weak);

        HasSuggestions(await InListAsync(api, draft)).Should().BeFalse("S3: gợi ý yếu cần tên khớp");
    }

    [Fact]
    public async Task ARecentlyConfirmedTransaction_IsSuggested_ButNotAfterTheWindow()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        await LoanAsync(api, "Nam");
        var income = await api.TransactionAsync(vcb, await api.CategoryAsync("income"), 500_000, "Credit", merchant: "NAM CHUYEN TIEN");

        HasSuggestions(await InListAsync(api, income)).Should().BeTrue();

        await api.InDbAsync(db => db.Transactions.Where(t => t.Id == income).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTimeOffset.UtcNow.AddHours(-73))));
        HasSuggestions(await InListAsync(api, income)).Should().BeFalse("D9: quá 72 giờ thì thôi gợi ý");
    }

    [Fact]
    public async Task ALinkedTransaction_ShowsItsDebtLink_AndNoSuggestions()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        var nam = await LoanAsync(api, "Nam");
        var income = await api.TransactionAsync(vcb, await api.CategoryAsync("income"), 500_000, "Credit", merchant: "NAM TRA NO");
        await api.ExpectAsync(HttpMethod.Post, $"/api/v1/loans/{nam}/repayments", new { transactionId = income }, HttpStatusCode.OK);

        var dto = await api.TransactionDetailAsync(income);

        dto.GetProperty("loanLink").GetProperty("loanId").GetGuid().Should().Be(nam);
        dto.GetProperty("loanLink").GetProperty("counterparty").GetString().Should().Be("Nam");
        dto.GetProperty("loanLink").GetProperty("kind").GetString().Should().Be("repayment");
        HasSuggestions(dto).Should().BeFalse();
    }

    [Fact]
    public async Task WrongDirection_TooLarge_OrNoOpenDebt_SuggestsNothing()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        await LoanAsync(api, "Nam", 300_000);
        await LoanAsync(api, "Nam", 5_000_000, "borrow");
        var draft = await NotificationDraftAsync(api, vcb, 500_000, "NAM TRA NO", DebtHintStrength.Strong);

        HasSuggestions(await InListAsync(api, draft)).Should().BeFalse("khoản cho vay chỉ còn 300k; khoản đi vay trả bằng tiền ra, không phải tiền vào");

        var stranger = await UserAsync();
        var strangerWallet = await stranger.WalletAsync();
        var lonely = await NotificationDraftAsync(stranger, strangerWallet, 500_000, "NAM TRA NO", DebtHintStrength.Strong);
        HasSuggestions(await InListAsync(stranger, lonely)).Should().BeFalse("khoản nợ của người khác không bao giờ là ứng viên");
    }

    /// <summary>Q15: thông báo không dấu "HOA TRA NO" gợi ý cả khoản của Hoa lẫn của Hòa.</summary>
    [Fact]
    public async Task Q15_AnUnaccentedNotification_SuggestsBothHoaAndHoa()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        var hoa = await LoanAsync(api, "Hoa", 800_000);
        var hoaAccented = await LoanAsync(api, "Hòa", 800_000);
        var draft = await NotificationDraftAsync(api, vcb, 500_000, "HOA TRA NO", null);

        var suggestions = (await InListAsync(api, draft)).GetProperty("loanSuggestions").EnumerateArray().Select(s => s.GetProperty("loanId").GetGuid());

        suggestions.Should().BeEquivalentTo([hoa, hoaAccented]);
    }

    /// <summary>S1: số tiền vượt gốc còn lại trong ngưỡng lãi vẫn được gợi ý, kèm phần lãi sẽ tách ra.</summary>
    [Fact]
    public async Task S1_ARepaymentWithInterest_IsSuggested_WithItsInterestPart()
    {
        var api = await UserAsync();
        var vcb = await api.WalletAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var nam = await LoanAsync(api, "Nam", 2_000_000, rate: 12, start: today.AddDays(-90));
        var draft = await NotificationDraftAsync(api, vcb, 2_060_000, "NAM TRA NO", DebtHintStrength.Strong);

        var first = (await InListAsync(api, draft)).GetProperty("loanSuggestions")[0];

        (first.GetProperty("loanId").GetGuid(), first.GetProperty("interestCents").GetInt64()).Should().Be((nam, 60_000L));
    }
}
