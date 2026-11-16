using System.Net.Http.Json;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Lời gọi HTTP và gieo dữ liệu dùng chung cho các test tích hợp bản trùng có liên kết (Phase 82). Dựa trên <see cref="RefundApi"/>.</summary>
internal sealed class DuplicateApi
{
    private readonly RefundApi _api;

    public DuplicateApi(WebApplicationFactory<FinMate.API.Program> factory)
    {
        _api = new RefundApi(factory);
    }

    public RefundApi Base => _api;

    public record SummaryData(Guid Id, long AmountCents, DateTimeOffset TransactedAt, string Source, string? MerchantName, string? CategoryName, string? FinancialAccountName);

    public record TxData(
        Guid Id, long AmountCents, string TransactionType, string Source, string Status, Guid FinancialAccountId, Guid? CategoryId,
        DateTimeOffset TransactedAt, long? BalanceAfterCents, string? MerchantName, string? Description,
        string? DuplicateStatus, SummaryData? DuplicateOf, List<SummaryData>? SuggestedDuplicates, List<string>? MergedSources);

    public static async Task<TxData> DataAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<TxData>>(RefundApi.JsonOptions))!.Data;

    public async Task<TxData> GetAsync(RefundApi.Session s, Guid id)
        => await DataAsync(await _api.SendAsync(HttpMethod.Get, $"/api/v1/transactions/{id}", s.Token));

    public async Task<List<TxData>> ListAsync(RefundApi.Session s, string query)
    {
        var response = await _api.SendAsync(HttpMethod.Get, $"/api/v1/transactions?{query}", s.Token);
        return (await response.Content.ReadFromJsonAsync<RefundApi.Envelope<List<TxData>>>(RefundApi.JsonOptions))!.Data;
    }

    public Task<HttpResponseMessage> NotDuplicateAsync(RefundApi.Session s, Guid id)
        => _api.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{id}/not-duplicate", s.Token);

    public Task<HttpResponseMessage> MergeAsync(RefundApi.Session s, Guid id, Guid with, Guid? categoryId = null)
        => _api.SendAsync(HttpMethod.Post, $"/api/v1/transactions/{id}/merge", s.Token, new { withTransactionId = with, categoryId });

    /// <summary>Một giao dịch chèn thẳng vào DB như kênh tương ứng tạo ra. Nháp không tác động số dư hay ngân sách.</summary>
    public Task<Guid> InsertAsync(
        RefundApi.Session s, long amount, DateTimeOffset at,
        TransactionSource source = TransactionSource.Notification, TransactionStatus status = TransactionStatus.Draft,
        Guid? account = null, Guid? category = null, Guid? duplicateOf = null, DuplicateStatus? duplicateStatus = null,
        TransactionType type = TransactionType.Debit, string? merchant = null, string? description = null)
        => _api.WithDbAsync(async db =>
        {
            var userId = await db.FinancialAccounts.Where(a => a.Id == s.Account).Select(a => a.UserId).SingleAsync();
            var now = DateTimeOffset.UtcNow;
            var tx = new Transaction
            {
                Id = Guid.NewGuid(), UserId = userId, FinancialAccountId = account ?? s.Account, CategoryId = category, AmountCents = amount,
                TransactionType = type, Source = source, Status = status, MerchantName = merchant, Description = description, TransactedAt = at,
                DuplicateOfTransactionId = duplicateOf, DuplicateStatus = duplicateStatus, CreatedAt = now, UpdatedAt = now,
            };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();
            return tx.Id;
        });
}
