using System.Net;
using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 81, luồng tự động: thông báo có <c>relation_hint</c> → nháp + gợi ý khoản chi gốc + push <c>choose_refund_link</c>; R3 (QR).</summary>
[Collection("Integration")]
public class RefundNotificationTests : IClassFixture<RefundApiFactory>
{
    internal const string Package = "vn.com.vng.zalopay";
    private readonly RefundApiFactory _factory;
    private readonly RefundApi _api;

    public RefundNotificationTests(RefundApiFactory factory)
    {
        _factory = factory;
        _api = new RefundApi(factory);
    }

    private record Analysis(Guid NotificationLogId, string Status, Guid? DraftTransactionId, string? RelationHint, List<RefundApi.SuggestionData>? SuggestedRelatedTransactions);

    internal static async Task<RefundApi.Session> WalletSessionAsync(RefundApi api, AuthApiFactory factory)
    {
        var s = await api.StartAsync();
        Guid provider;
        using (var scope = factory.Services.CreateScope())
        {
            provider = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().ProviderConfigs.Where(p => p.ProviderKey == "zalopay").Select(p => p.Id).SingleAsync();
        }

        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", s.Token,
            new CreateFinancialAccountRequest("ZaloPay", AccountType.EWallet, provider, 5_000_000));
        var wallet = (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
        return s with { Account = wallet };
    }

    internal static async Task<HttpResponseMessage> NotifyAsync(RefundApi api, RefundApi.Session s, long amount, string? hint, string type = "credit", double confidence = 0.95)
    {
        FakeAIServiceClient.SetScenario(Package, new AnalyzeResponse(
            "financial",
            new ClassifierResult("financial", 0.97),
            new ExtractionResult(amount, type, "Shopee", "HOAN TIEN DON HANG", DateTimeOffset.UtcNow, null, confidence, hint),
            new CategorizationResult("income", confidence),
            new DuplicateResult(false, null),
            new ModelVersions("1", "1", "1"),
            10));
        return await api.SendAsync(HttpMethod.Post, "/api/v1/notifications/analyze", s.Token, new
        {
            packageName = Package,
            notificationTitle = "ZaloPay",
            notificationBody = $"+{amount} HOAN TIEN DON HANG {Guid.NewGuid():N}",
            receivedAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task<Guid> UserIdAsync(RefundApi.Session s)
        => await _api.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());

    [Fact]
    public async Task RefundHint_GivesADraftWithTheRightSuggestion_AndAskToChoose_EvenAtHighConfidence()
    {
        var s = await WalletSessionAsync(_api, _factory);
        var coffee = await _api.SpendAsync(s, 950_000, merchant: "Highlands");
        var shoes = await _api.SpendAsync(s, 900_000, merchant: "Shopee");
        var userId = await UserIdAsync(s);

        var analysis = (await (await NotifyAsync(_api, s, 900_000, "refund", confidence: 0.99)).Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;

        analysis.RelationHint.Should().Be("Refund");
        analysis.SuggestedRelatedTransactions!.Select(x => x.TransactionId).Should().StartWith(shoes.Id).And.Contain(coffee.Id);
        var draft = await _api.GetAsync(s, analysis.DraftTransactionId!.Value);
        draft.Status.Should().Be("Draft", "hệ thống không bao giờ tự liên kết hay tự xác nhận (BR-RF-13)");
        draft.Relation.Should().BeNull();
        draft.SuggestedRelatedTransactions!.First().TransactionId.Should().Be(shoes.Id);
        _factory.Push.Sent.Where(p => p.UserId == userId).Should().ContainSingle(p => p.Action == "choose_refund_link");
    }

    [Fact]
    public async Task ReversalHint_OnlyLooksBackThreeDays()
    {
        var s = await WalletSessionAsync(_api, _factory);
        await _api.SpendAsync(s, 500_000, DateTimeOffset.UtcNow.AddDays(-5), merchant: "Shopee");
        var recent = await _api.SpendAsync(s, 500_000, DateTimeOffset.UtcNow.AddDays(-1), merchant: "Shopee");

        var analysis = (await (await NotifyAsync(_api, s, 500_000, "reversal")).Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;

        analysis.SuggestedRelatedTransactions!.Select(x => x.TransactionId).Should().Equal(recent.Id);
    }

    [Fact]
    public async Task HintWithoutCandidates_StillAsksToChoose_AndNeverSuggestsSomeoneElses()
    {
        var stranger = await WalletSessionAsync(_api, _factory);
        await _api.SpendAsync(stranger, 900_000, merchant: "Shopee");
        var s = await WalletSessionAsync(_api, _factory);
        var userId = await UserIdAsync(s);

        var analysis = (await (await NotifyAsync(_api, s, 900_000, "refund")).Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;

        analysis.SuggestedRelatedTransactions.Should().BeEmpty();
        _factory.Push.Sent.Where(p => p.UserId == userId).Should().ContainSingle(p => p.Action == "choose_refund_link");
    }

    [Fact]
    public async Task OldAiWithoutHint_BehavesAsBefore()
    {
        var s = await WalletSessionAsync(_api, _factory);
        var userId = await UserIdAsync(s);

        var analysis = (await (await NotifyAsync(_api, s, 900_000, null, confidence: 0.95)).Content.ReadFromJsonAsync<RefundApi.Envelope<Analysis>>(RefundApi.JsonOptions))!.Data;

        analysis.RelationHint.Should().BeNull();
        analysis.SuggestedRelatedTransactions.Should().BeNull();
        _factory.Push.Sent.Where(p => p.UserId == userId).Should().ContainSingle(p => p.Action == "confirm_one_tap");
    }

    [Fact]
    public async Task RelationFilter_ListsOnlyRefunds()
    {
        var s = await _api.StartAsync();
        var shoes = await _api.SpendAsync(s, 900_000);
        var refund = await RefundApi.DataAsync(await _api.RefundAsync(s, shoes.Id, new { amountCents = 100_000 }));
        await _api.EarnAsync(s, 1_000_000);

        var response = await _api.SendAsync(HttpMethod.Get, "/api/v1/transactions?relation=Refund", s.Token);
        var items = (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<List<RefundApi.TransactionData>>>(RefundApi.JsonOptions))!.Data;

        items.Select(i => i.Id).Should().Equal(refund.Id);
        var all = (await (await _api.SendAsync(HttpMethod.Get, "/api/v1/transactions", s.Token)).Content.ReadFromJsonAsync<RefundApi.Envelope<List<RefundApi.TransactionData>>>(RefundApi.JsonOptions))!.Data;
        all.Single(i => i.Id == shoes.Id).RefundedCents.Should().Be(100_000);
    }

    [Fact]
    public async Task R3_AMatchedQrIntentStaysMatched_WhenItsTransferIsReversed()
    {
        var s = await _api.StartAsync();
        var userId = await UserIdAsync(s);
        var transfer = await _api.SpendAsync(s, 500_000, merchant: "NGUYEN VAN A");
        var intentId = await _api.WithDbAsync(async db =>
        {
            var intent = new QrTransferIntent
            {
                Id = Guid.NewGuid(), UserId = userId, ReceiverBankBin = "970436", ReceiverAccountNumber = "0123456789", ReceiverName = "NGUYEN VAN A",
                SenderBankBin = "970422", AmountCents = 500_000, Status = QrTransferStatus.Matched, ScannedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                MatchedTransactionId = transfer.Id, MatchedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.QrTransferIntents.Add(intent);
            await db.SaveChangesAsync();
            return intent.Id;
        });

        (await _api.RefundAsync(s, transfer.Id, new { relation = "Reversal" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await _api.WithDbAsync(db => db.QrTransferIntents.AsNoTracking().SingleAsync(i => i.Id == intentId));
        after.Status.Should().Be(QrTransferStatus.Matched);
        after.MatchedTransactionId.Should().Be(transfer.Id);

        using var scope = _factory.Services.CreateScope();
        var again = await scope.ServiceProvider.GetRequiredService<IQrTransferRepository>()
            .FindPendingAsync(userId, "970422", 500_000, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
        again.Should().BeNull("một chi lại cùng số tiền không được khớp ý định cũ lần hai");
    }
}
