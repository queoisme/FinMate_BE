using System.Net;
using System.Net.Http.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 83, phần B: <c>merge/preview</c> và <c>convert-to-transfer/preview</c> trả đúng kết quả lệnh thật sẽ ra mà không ghi gì; mọi lượt gộp
/// (kể cả gộp bản trùng của Phase 82) ghi ảnh chụp; ảnh chụp quá hạn lưu bị dọn (S5).
/// </summary>
[Collection("Integration")]
public class PreviewAndSnapshotTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly TransferApi _api;

    public PreviewAndSnapshotTests(AuthApiFactory factory)
    {
        _factory = factory;
        _api = new TransferApi(factory);
    }

    private record BalanceChange(Guid AccountId, string AccountName, long BalanceBeforeCents, long BalanceAfterCents);
    private record PreviewData(TransferApi.TxData Survivor, TransferApi.TxData? Fee, List<BalanceChange> BalanceChanges, List<string>? Warnings);

    private static async Task<PreviewData> PreviewAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<PreviewData>>(RefundApi.JsonOptions))!.Data;

    private Task<int> SnapshotCountAsync(Guid userId) => _api.Base.WithDbAsync(db => db.TransactionMergeSnapshots.CountAsync(s => s.UserId == userId));

    [Fact]
    public async Task ConvertPreview_ShowsWhatWillHappen_WritesNothing_AndMatchesTheRealConversion()
    {
        var w = await _api.StartAsync();
        var debit = await _api.DebitAsync(w, 1_003_300, description: "nap MoMo");
        var credit = await _api.CreditAsync(w, 1_000_000);
        var body = new { counterAccountId = w.Momo, pairedTransactionId = credit };

        var response = await _api.Base.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{debit}/convert-to-transfer/preview", w.S.Token, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await PreviewAsync(response);
        preview.Survivor.TransactionType.Should().Be("Transfer");
        preview.Fee!.AmountCents.Should().Be(3_300);
        preview.BalanceChanges.Should().ContainSingle(c => c.AccountId == w.Vcb).Which.BalanceAfterCents.Should().Be(5_000_000 - 1_003_300);
        preview.BalanceChanges.Should().ContainSingle(c => c.AccountId == w.Momo).Which.BalanceAfterCents.Should().Be(1_000_000);

        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(5_000_000, "xem trước không ghi gì");
        (await _api.GetAsync(w.S, credit)).TransactionType.Should().Be("Credit");
        (await SnapshotCountAsync(w.UserId)).Should().Be(0);

        var real = await TransferApi.ConversionAsync(await _api.ConvertAsync(w.S, debit, w.Momo, credit));
        real.Transfer.Id.Should().Be(preview.Survivor.Id);
        real.Transfer.AmountCents.Should().Be(preview.Survivor.AmountCents);
        real.Fee!.AmountCents.Should().Be(preview.Fee.AmountCents);
        (await _api.Base.BalanceAsync(w.Vcb)).Should().Be(preview.BalanceChanges.Single(c => c.AccountId == w.Vcb).BalanceAfterCents);
    }

    [Fact]
    public async Task MergePreview_MatchesTheRealMerge_AndWritesNothing()
    {
        var w = await _api.StartAsync();
        var chat = await _api.Base.SpendAsync(w.S, 50_000, DateTimeOffset.UtcNow.AddMinutes(-2), w.S.Food, "trà sữa");
        var bank = await _api.Dup.InsertAsync(w.S, 50_000, DateTimeOffset.UtcNow.AddMinutes(-1), account: w.Vcb,
            duplicateOf: chat.Id, duplicateStatus: DuplicateStatus.Suspected);
        var cash = await _api.Base.BalanceAsync(w.Cash);

        var preview = await PreviewAsync(await _api.Base.SendAsync(
            HttpMethod.Post, $"/api/v1/transactions/{bank}/merge/preview", w.S.Token, new { withTransactionId = chat.Id }));

        preview.Survivor.Id.Should().Be(chat.Id);
        preview.Survivor.FinancialAccountId.Should().Be(w.Vcb);
        preview.BalanceChanges.Should().ContainSingle(c => c.AccountId == w.Cash).Which.BalanceAfterCents.Should().Be(cash + 50_000);
        (await _api.Base.BalanceAsync(w.Cash)).Should().Be(cash);
        (await _api.Dup.GetAsync(w.S, bank)).DuplicateStatus.Should().Be("Suspected", "bản bị gộp vẫn còn nguyên");

        var merged = await DuplicateApi.DataAsync(await _api.Dup.MergeAsync(w.S, bank, chat.Id));
        merged.FinancialAccountId.Should().Be(preview.Survivor.FinancialAccountId);
        merged.TransactedAt.Should().Be(preview.Survivor.TransactedAt);
        (await _api.Base.BalanceAsync(w.Cash)).Should().Be(cash + 50_000);
    }

    [Fact]
    public async Task Previews_ReturnTheSameErrorsAsTheRealCommands()
    {
        var w = await _api.StartAsync();
        var a = await _api.DebitAsync(w, 50_000);
        var b = await _api.DebitAsync(w, 50_001, account: w.Momo);

        var merge = await _api.Base.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{a}/merge/preview", w.S.Token, new { withTransactionId = b });
        var convert = await _api.Base.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{Guid.NewGuid()}/convert-to-transfer/preview", w.S.Token,
            new { counterAccountId = w.Momo });

        (await RefundApi.ErrorAsync(merge)).Should().Be("MERGE_NOT_ALLOWED");
        convert.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ADuplicateMerge_WritesASnapshot()
    {
        var w = await _api.StartAsync();
        var chat = await _api.Base.SpendAsync(w.S, 50_000, DateTimeOffset.UtcNow.AddMinutes(-2), w.S.Food, "trà sữa");
        var bank = await _api.Dup.InsertAsync(w.S, 50_000, DateTimeOffset.UtcNow.AddMinutes(-1), account: w.Vcb,
            duplicateOf: chat.Id, duplicateStatus: DuplicateStatus.Suspected);

        await _api.Dup.MergeAsync(w.S, bank, chat.Id);

        var snapshot = await _api.Base.WithDbAsync(db => db.TransactionMergeSnapshots.AsNoTracking().SingleAsync(s => s.UserId == w.UserId));
        snapshot.Kind.Should().Be(MergeSnapshotKind.DuplicateMerge);
        snapshot.SurvivorId.Should().Be(chat.Id);
        snapshot.MergedId.Should().Be(bank);
        snapshot.Before.Should().Contain(bank.ToString()).And.Contain(w.Cash.ToString());
    }

    [Fact]
    public async Task SnapshotsOlderThanTheRetention_ArePurgedByTheNightlyJob()
    {
        var w = await _api.StartAsync();
        await _api.Base.WithDbAsync(async db =>
        {
            db.TransactionMergeSnapshots.AddRange(
                new TransactionMergeSnapshot { Id = Guid.NewGuid(), UserId = w.UserId, SurvivorId = Guid.NewGuid(), Kind = MergeSnapshotKind.TransferConvert, CreatedAt = DateTimeOffset.UtcNow.AddDays(-400) },
                new TransactionMergeSnapshot { Id = Guid.NewGuid(), UserId = w.UserId, SurvivorId = Guid.NewGuid(), Kind = MergeSnapshotKind.TransferConvert, CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) });
            return await db.SaveChangesAsync();
        });

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationBodyPurgeJob>().RunAsync(CancellationToken.None);
        }

        (await SnapshotCountAsync(w.UserId)).Should().Be(1, "chỉ còn ảnh chụp trong 365 ngày");
    }
}
