using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

/// <summary>
/// Đọc dữ liệu xuất (Phase 91). <c>IgnoreQueryFilters</c> có chủ ý ở các truy vấn phụ lấy tên: giao dịch của một ví đã xoá vẫn là của user và vẫn cần
/// tên ví. Mọi truy vấn gốc đều lọc <c>user_id</c>; truy vấn phụ chỉ đi theo id đã nằm trên giao dịch của chính user.
/// </summary>
public sealed class DataExportRepository(FinMateDbContext db) : IDataExportRepository
{
    public Task<int> CountTransactionsAsync(Guid userId, TransactionExportFilter filter, CancellationToken ct = default)
        => Filtered(userId, filter).CountAsync(ct);

    public IAsyncEnumerable<TransactionExportRow> StreamTransactionsAsync(Guid userId, TransactionExportFilter filter, CancellationToken ct = default)
        => (from t in Filtered(userId, filter)
            orderby t.TransactedAt, t.Id
            select new TransactionExportRow
            {
                Id = t.Id,
                TransactedAt = t.TransactedAt,
                Type = t.TransactionType,
                AmountCents = t.AmountCents,
                AccountName = db.FinancialAccounts.IgnoreQueryFilters().Where(a => a.Id == t.FinancialAccountId).Select(a => a.AccountName).FirstOrDefault() ?? string.Empty,
                CounterAccountName = db.FinancialAccounts.IgnoreQueryFilters().Where(a => a.Id == t.CounterAccountId).Select(a => a.AccountName).FirstOrDefault(),
                CategoryName = db.Categories.IgnoreQueryFilters().Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault()
                    ?? db.GroupCategories.IgnoreQueryFilters().Where(c => c.Id == t.GroupCategoryId).Select(c => c.Name).FirstOrDefault(),
                CategorySlug = db.Categories.IgnoreQueryFilters().Where(c => c.Id == t.CategoryId).Select(c => c.Slug).FirstOrDefault(),
                MerchantName = t.MerchantName,
                Description = t.Description,
                Source = t.Source,
                Status = t.Status,
                Relation = t.Relation,
                IsReversed = t.IsReversed,
                RelatedTransactionId = t.RelatedTransactionId,
                ReversedByTransactionId = t.IsReversed
                    ? db.Transactions.Where(r => r.UserId == userId && r.RelatedTransactionId == t.Id && r.Relation == TransactionRelation.Reversal)
                        .Select(r => (Guid?)r.Id).FirstOrDefault()
                    : null,
                LoanId = (from r in db.LoanRepayments
                          join l in db.Loans on r.LoanId equals l.Id
                          where l.UserId == userId && l.DeletedAt == null && r.DeletedAt == null && (r.TransactionId == t.Id || r.InterestTransactionId == t.Id)
                          select (Guid?)l.Id).FirstOrDefault(),
                IsLoanInterest = db.LoanRepayments.Any(r => r.DeletedAt == null && r.InterestTransactionId == t.Id),
                GroupFundEntryId = t.GroupFundEntryId,
                DuplicateStatus = t.DuplicateStatus,
                DuplicateOfTransactionId = t.DuplicateOfTransactionId,
                MergedSources = t.MergedSources,
                GroupName = t.GroupId == null ? null : db.Groups.IgnoreQueryFilters().Where(g => g.Id == t.GroupId).Select(g => g.Name).FirstOrDefault(),
                DeletedAt = t.DeletedAt,
                MergedIntoTransactionId = t.MergedIntoTransactionId,
            })
            .AsNoTracking()
            .AsAsyncEnumerable();

    private IQueryable<Transaction> Filtered(Guid userId, TransactionExportFilter filter)
    {
        var statuses = filter.Statuses.ToArray();
        var query = db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == userId && statuses.Contains(t.Status));
        if (filter.DeletedOnly)
        {
            query = query.Where(t => t.DeletedAt != null);
        }
        else if (!filter.IncludeDeleted)
        {
            query = query.Where(t => t.DeletedAt == null);
        }
        if (filter.Start is { } start)
        {
            query = query.Where(t => t.TransactedAt >= start);
        }

        if (filter.End is { } end)
        {
            query = query.Where(t => t.TransactedAt < end);
        }

        if (filter.AccountId is { } account)
        {
            query = query.Where(t => t.FinancialAccountId == account || t.CounterAccountId == account);
        }

        if (filter.CategoryId is { } category)
        {
            query = query.Where(t => t.CategoryId == category);
        }

        return query;
    }
}
