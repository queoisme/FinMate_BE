using System.Net;
using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Tests.Integration.Controllers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Scenarios;

/// <summary>
/// Kịch bản đầu-cuối bắt buộc của Phase 82, qua đúng các endpoint app gọi (ngân sách Ăn uống 1.000.000đ):
/// (a) chat "trà sữa 50k" vào ví tiền mặt, 1 phút sau thông báo VCB −50.000 → nháp nghi trùng → gộp → một giao dịch;
/// (b) hai thông báo −35.000 cách 3 phút, số dư sau khác nhau → hai nháp thường;
/// (c) cùng thông báo đó về lại với số dư giống hệt → bỏ âm thầm, log <c>duplicate</c>;
/// (d) hai thông báo −35.000 không có số dư → nháp thứ hai nghi trùng → "Không trùng" → xác nhận cả hai, không bị gắn cờ bất thường trùng.
/// </summary>
[Collection("Integration")]
public class DuplicateScenarioTests : IClassFixture<RefundApiFactory>
{
    private const string VcbPackage = "com.VCB";
    private readonly RefundApiFactory _factory;
    private readonly DuplicateApi _api;

    public DuplicateScenarioTests(RefundApiFactory factory)
    {
        _factory = factory;
        _api = new DuplicateApi(factory);
    }

    private record Analysis(Guid NotificationLogId, string Status, Guid? DraftTransactionId, Guid? DuplicateOfTransactionId);
    private record ChatResult(string Action, RefundApi.TransactionData? Transaction);

    private sealed record Setup(RefundApi.Session S, Guid UserId, Guid Cash, Guid Vcb, Guid Budget);

    private async Task<Setup> StartAsync()
    {
        var s = await _api.Base.StartAsync();
        Guid provider;
        using (var scope = _factory.Services.CreateScope())
        {
            provider = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().ProviderConfigs
                .Where(p => p.ProviderKey == "vietcombank").Select(p => p.Id).SingleAsync();
        }

        var response = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", s.Token,
            new CreateFinancialAccountRequest("VCB", AccountType.Bank, provider, 2_000_000));
        var vcb = (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        var budget = await _api.Base.BudgetAsync(s, 1_000_000, s.Food);
        var userId = await _api.Base.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());
        return new Setup(s, userId, s.Account, vcb, budget);
    }

    private async Task<Analysis> NotifyVcbAsync(Setup x, long amount, DateTimeOffset at, long? balanceAfter)
    {
        FakeAIServiceClient.SetScenario(VcbPackage, new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(amount, "debit", "PHUC LONG", "TT QR", at, balanceAfter, 0.95),
            new CategorizationResult("food", 0.95),
            new DuplicateResult(false, null),
            new ModelVersions("1", "1", "1"),
            10));
        var response = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/notifications/analyze", x.S.Token, new
        {
            packageName = VcbPackage,
            notificationTitle = "VCB",
            notificationBody = $"TK 0011: -{amount}VND {Guid.NewGuid():N}",
            receivedAt = at,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;
    }

    private List<string?> ActionsFor(Guid userId) => _factory.Push.Sent.Where(p => p.UserId == userId).Select(p => p.Action).ToList();

    [Fact]
    public async Task A_ChatThenBankNotification_MergeLeavesOneTransactionOnTheBankWallet()
    {
        var x = await StartAsync();
        var cashStart = await _api.Base.BalanceAsync(x.Cash);
        var vcbStart = await _api.Base.BalanceAsync(x.Vcb);
        var text = $"trà sữa 50k {Guid.NewGuid():N}";
        FakeAIServiceClient.SetChat(text, new ChatParseResponse("add_expense", "Đã ghi.",
            new ChatParseEntry("expense", 50_000, "trà sữa", null, "food", null, true, false), null, 5));

        var chatResponse = await _api.Base.SendAsync(HttpMethod.Post, "/api/v1/transactions/chat", x.S.Token, new { text, financialAccountId = x.Cash });
        var chat = (await chatResponse.Content.ReadFromJsonAsync<RefundApi.Envelope<ChatResult>>(RefundApi.JsonOptions))!.Data;
        chat.Action.Should().Be("saved");
        var chatId = chat.Transaction!.Id;

        var bankAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var analysis = await NotifyVcbAsync(x, 50_000, bankAt, 1_950_000);

        analysis.DraftTransactionId.Should().NotBeNull("thông báo không còn bị bỏ âm thầm");
        analysis.DuplicateOfTransactionId.Should().Be(chatId);
        var draft = await _api.GetAsync(x.S, analysis.DraftTransactionId!.Value);
        draft.Status.Should().Be("Draft");
        draft.DuplicateStatus.Should().Be("Suspected");
        draft.DuplicateOf!.Id.Should().Be(chatId);
        ActionsFor(x.UserId).Should().Contain("review_duplicate").And.NotContain("confirm_one_tap");

        var merged = await _api.MergeAsync(x.S, draft.Id, chatId);

        merged.StatusCode.Should().Be(HttpStatusCode.OK);
        var survivor = await DuplicateApi.DataAsync(merged);
        var live = await _api.ListAsync(x.S, "limit=100");
        live.Should().ContainSingle().Which.Id.Should().Be(survivor.Id);
        survivor.FinancialAccountId.Should().Be(x.Vcb);
        survivor.TransactedAt.Should().BeCloseTo(bankAt, TimeSpan.FromMilliseconds(1));
        survivor.CategoryId.Should().Be(x.S.Food);
        survivor.Status.Should().Be("Confirmed");
        survivor.MergedSources.Should().BeEquivalentTo(["manual", "notification"]);
        (await _api.Base.BalanceAsync(x.Cash)).Should().Be(cashStart, "ví tiền mặt được hoàn 50k");
        (await _api.Base.BalanceAsync(x.Vcb)).Should().Be(vcbStart - 50_000);
        (await _api.Base.PeriodAsync(x.Budget)).SpentCents.Should().Be(50_000);
    }

    [Fact]
    public async Task B_TwoPurchasesThreeMinutesApart_DifferentBalances_AreTwoPlainDrafts()
    {
        var x = await StartAsync();
        var at = DateTimeOffset.UtcNow;

        var first = await NotifyVcbAsync(x, 35_000, at, 1_965_000);
        var second = await NotifyVcbAsync(x, 35_000, at.AddMinutes(3), 1_930_000);

        first.DraftTransactionId.Should().NotBeNull();
        second.DraftTransactionId.Should().NotBeNull("lần mua thứ hai không còn bị mất");
        second.DuplicateOfTransactionId.Should().BeNull();
        (await _api.GetAsync(x.S, second.DraftTransactionId!.Value)).DuplicateStatus.Should().BeNull();
        (await _api.ListAsync(x.S, "duplicateStatus=Suspected")).Should().BeEmpty();
    }

    [Fact]
    public async Task C_TheSameNotificationAgainWithTheSameBalance_IsDroppedAndLoggedDuplicate()
    {
        var x = await StartAsync();
        var at = DateTimeOffset.UtcNow;
        var first = await NotifyVcbAsync(x, 35_000, at, 1_965_000);

        var again = await NotifyVcbAsync(x, 35_000, at.AddMinutes(1), 1_965_000);

        again.Status.Should().Be("Duplicate");
        again.DraftTransactionId.Should().BeNull();
        again.DuplicateOfTransactionId.Should().Be(first.DraftTransactionId);
        var log = await _api.Base.WithDbAsync(db => db.NotificationLogs.AsNoTracking().SingleAsync(l => l.Id == again.NotificationLogId));
        log.Status.Should().Be(NotificationLogStatus.Duplicate);
        (await _api.ListAsync(x.S, "limit=100")).Should().ContainSingle();
    }

    [Theory]
    [InlineData(35_000)]
    [InlineData(350_000)]   // trên ngưỡng 200k của phát hiện bất thường: chứng minh cặp đã bác thật sự không bị gắn cờ
    public async Task D_NoBalance_SecondIsSuspected_NotDuplicate_BothConfirm_NoDuplicateAnomaly(long amount)
    {
        var x = await StartAsync();
        var at = DateTimeOffset.UtcNow;
        var first = await NotifyVcbAsync(x, amount, at, null);
        var second = await NotifyVcbAsync(x, amount, at.AddMinutes(3), null);

        second.DuplicateOfTransactionId.Should().Be(first.DraftTransactionId);
        (await _api.GetAsync(x.S, second.DraftTransactionId!.Value)).DuplicateStatus.Should().Be("Suspected");

        (await _api.NotDuplicateAsync(x.S, second.DraftTransactionId!.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.Base.ConfirmAsync(x.S, first.DraftTransactionId!.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _api.Base.ConfirmAsync(x.S, second.DraftTransactionId!.Value)).StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmed = await _api.ListAsync(x.S, "status=Confirmed");
        confirmed.Should().HaveCount(2);
        using var scope = _factory.Services.CreateScope();
        var anomalies = await scope.ServiceProvider.GetRequiredService<IAnomalyService>().ScanAsync(x.UserId);
        anomalies.Should().NotContain(a => a.Anomaly.Kind == AnomalyKind.Duplicate);
    }
}
