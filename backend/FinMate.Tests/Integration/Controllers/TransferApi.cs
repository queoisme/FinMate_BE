using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Lời gọi HTTP và gieo dữ liệu dùng chung cho các test tích hợp chuyển ví (Phase 83). Dựa trên <see cref="DuplicateApi"/>.</summary>
internal sealed class TransferApi
{
    private readonly WebApplicationFactory<FinMate.API.Program> _factory;

    public TransferApi(WebApplicationFactory<FinMate.API.Program> factory)
    {
        _factory = factory;
        Dup = new DuplicateApi(factory);
    }

    public DuplicateApi Dup { get; }

    public RefundApi Base => Dup.Base;

    /// <summary>Phiên có ví Tiền mặt (từ <see cref="RefundApi.StartAsync"/>), ví VCB và ví MoMo gắn đúng provider.</summary>
    public sealed record Wallets(RefundApi.Session S, Guid UserId, Guid Cash, Guid Vcb, Guid Momo);

    public record TransferSuggestionData(string Kind, PairedData? PairedTransaction, List<AccountData> CandidateAccounts, long? FeeCents);
    public record PairedData(Guid Id, long AmountCents, DateTimeOffset TransactedAt, Guid FinancialAccountId, string? FinancialAccountName);
    public record AccountData(Guid Id, string Name, string AccountType);

    public record TxData(
        Guid Id, long AmountCents, string TransactionType, string Source, string Status, Guid FinancialAccountId, Guid? CounterAccountId, Guid? CategoryId,
        DateTimeOffset TransactedAt, string? Description, List<string>? MergedSources, TransferSuggestionData? TransferSuggestion);

    public record ConversionData(TxData Transfer, TxData? Fee, List<string>? Warnings);

    public async Task<Wallets> StartAsync(long vcbBalance = 5_000_000, long momoBalance = 0)
    {
        var s = await Base.StartAsync();
        var vcb = await AccountAsync(s, "VCB", AccountType.Bank, "vietcombank", vcbBalance);
        var momo = await AccountAsync(s, "MoMo", AccountType.EWallet, "momo", momoBalance);
        var userId = await Base.WithDbAsync(db => db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync());
        return new Wallets(s, userId, s.Account, vcb, momo);
    }

    public async Task<Guid> AccountAsync(RefundApi.Session s, string name, AccountType type, string? providerKey, long balance = 0)
    {
        Guid? provider = null;
        if (providerKey is not null)
        {
            using var scope = _factory.Services.CreateScope();
            provider = await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().ProviderConfigs
                .Where(p => p.ProviderKey == providerKey).Select(p => p.Id).SingleAsync();
        }

        var response = await Base.SendAsync(HttpMethod.Post, "/api/v1/financial-accounts", s.Token, new CreateFinancialAccountRequest(name, type, provider, balance));
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
    }

    public Task<HttpResponseMessage> ConvertAsync(RefundApi.Session s, Guid id, Guid counterAccountId, Guid? pairedTransactionId = null)
        => Base.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{id}/convert-to-transfer", s.Token, new { counterAccountId, pairedTransactionId });

    public Task<HttpResponseMessage> NotTransferAsync(RefundApi.Session s, Guid id)
        => Base.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{id}/not-transfer", s.Token);

    public static async Task<ConversionData> ConversionAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<ConversionData>>(RefundApi.JsonOptions))!.Data;

    public async Task<TxData> GetAsync(RefundApi.Session s, Guid id)
    {
        var response = await Base.SendAsync(HttpMethod.Get, $"/api/v1/transactions/{id}", s.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<TxData>>(RefundApi.JsonOptions))!.Data;
    }

    public Task<Guid> DebitAsync(Wallets w, long amount, Guid? account = null, string? description = null, TransactionStatus status = TransactionStatus.Draft,
        DateTimeOffset? at = null, Guid? category = null, TransactionSource source = TransactionSource.Notification)
        => Dup.InsertAsync(w.S, amount, at ?? DateTimeOffset.UtcNow.AddMinutes(-5), source, status, account ?? w.Vcb, category, description: description);

    public Task<Guid> CreditAsync(Wallets w, long amount, Guid? account = null, string? description = null, TransactionStatus status = TransactionStatus.Draft,
        DateTimeOffset? at = null, Guid? category = null)
        => Dup.InsertAsync(w.S, amount, at ?? DateTimeOffset.UtcNow.AddMinutes(-4), TransactionSource.Notification, status, account ?? w.Momo, category,
            type: TransactionType.Credit, description: description);

    /// <summary>Chuyển tiền ghi tay (<c>POST /transactions/transfer</c>), như app lưu sau câu chat "nạp 500k vào momo".</summary>
    public async Task<Guid> ManualTransferAsync(Wallets w, Guid from, Guid to, long amount, DateTimeOffset? at = null)
    {
        var response = await Base.SendAsync(HttpMethod.Post, "/api/v1/transactions/transfer", w.S.Token,
            new CreateTransferRequest(from, to, amount, at ?? DateTimeOffset.UtcNow.AddMinutes(-5), "nạp momo"));
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<RefundApi.IdData>>(RefundApi.JsonOptions))!.Data.Id;
    }

    /// <summary>Giao dịch đã xác nhận được chèn thẳng vào DB thì chưa cộng vào số dư: cộng tay như đường xác nhận thật.</summary>
    public Task ApplyAsync(Guid account, long signedAmount)
        => Base.WithDbAsync(async db =>
        {
            var a = await db.FinancialAccounts.SingleAsync(x => x.Id == account);
            a.BalanceCents += signedAmount;
            return await db.SaveChangesAsync();
        });
}
