using FinMate.Application.DataExport.Package;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.DataExport;

public sealed partial class PersonalDataReader
{
    private async Task<IEnumerable<PackageSection>> MoneySectionsAsync(Guid userId, CancellationToken ct) =>
    [
        new PackageSection("accounts.json", await AccountsAsync(userId, ct), IsList: true),
        new PackageSection("categories.json", await CategoriesAsync(userId, ct), IsList: false),
        new PackageSection("category_rules.json", await CategoryRulesAsync(userId, ct), IsList: false),
        new PackageSection("bank_notifications.json", await BankNotificationsAsync(userId, ct), IsList: true),
        new PackageSection("receipts.json", await ReceiptsAsync(userId, ct), IsList: true),
        new PackageSection("anomalies.json", await AnomaliesAsync(userId, ct), IsList: true),
        new PackageSection("qr_transfers.json", await QrTransfersAsync(userId, ct), IsList: true),
    ];

    private async Task<object> AccountsAsync(Guid userId, CancellationToken ct)
        => await db.FinancialAccounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                id = a.Id, name = a.AccountName, type = a.AccountType, provider = a.ProviderConfig == null ? null : a.ProviderConfig.DisplayName,
                packageName = a.PackageName, isMonitored = a.IsMonitored, balanceCents = a.BalanceCents, includeInForecast = a.IncludeInForecast,
                createdAt = a.CreatedAt, updatedAt = a.UpdatedAt, deletedAt = a.DeletedAt,
            }).ToListAsync(ct);

    private async Task<object> CategoriesAsync(Guid userId, CancellationToken ct) => new
    {
        own = await db.Categories.IgnoreQueryFilters().AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id, name = c.Name, slug = c.Slug, kind = c.Kind, iconName = c.IconName, color = c.Color, parentCategoryId = c.ParentCategoryId,
                suggestedBudgetCents = c.SuggestedBudgetCents, isFavorite = c.IsFavorite, isActive = c.IsActive, createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt, deletedAt = c.DeletedAt,
            }).ToListAsync(ct),
        hidden = await (from h in db.UserHiddenCategories
                        join c in db.Categories.IgnoreQueryFilters() on h.CategoryId equals c.Id
                        where h.UserId == userId
                        orderby h.CreatedAt
                        select new { categoryId = c.Id, name = c.Name, hiddenAt = h.CreatedAt }).AsNoTracking().ToListAsync(ct),
    };

    private async Task<object> CategoryRulesAsync(Guid userId, CancellationToken ct) => new
    {
        merchants = await db.MerchantCategoryOverrides.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                merchantKey = m.MerchantKey, displayName = m.DisplayName, categoryId = m.CategoryId, categoryName = m.Category!.Name,
                createdAt = m.CreatedAt, updatedAt = m.UpdatedAt,
            }).ToListAsync(ct),
        counterparties = await db.CounterpartyCategoryOverrides.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                bankBin = c.BankBin, accountNumber = c.AccountNumber, displayName = c.DisplayName, categoryId = c.CategoryId, categoryName = c.Category!.Name,
                createdAt = c.CreatedAt, updatedAt = c.UpdatedAt,
            }).ToListAsync(ct),
    };

    /// <summary>BR-EXP-05/08: không tiêu đề, không nội dung, không mã băm, không lỗi kỹ thuật; chỉ package, thời điểm, kết quả, giao dịch tạo ra.</summary>
    private async Task<object> BankNotificationsAsync(Guid userId, CancellationToken ct)
        => await db.NotificationLogs.AsNoTracking().Where(n => n.UserId == userId).OrderBy(n => n.ReceivedAt)
            .Select(n => new
            {
                packageName = n.PackageName, receivedAt = n.ReceivedAt, status = n.Status, processedAt = n.ProcessedAt,
                transactionId = db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == userId && t.NotificationLogId == n.Id).Select(t => (Guid?)t.Id).FirstOrDefault(),
            }).ToListAsync(ct);

    private async Task<object> ReceiptsAsync(Guid userId, CancellationToken ct)
    {
        var scans = await db.ReceiptScans.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Id, s.AmountCents, s.MerchantName, s.TransactedAt, s.CategorySlug, s.NeedsReview, s.CreatedAt }).ToListAsync(ct);
        var ids = scans.Select(s => s.Id).ToList();
        var items = (await db.ReceiptScanItems.AsNoTracking().Where(i => ids.Contains(i.ScanId)).OrderBy(i => i.Position)
            .Select(i => new { i.ScanId, i.Position, i.Name, i.AmountCents, i.CategorySlug, i.ParentPosition }).ToListAsync(ct)).ToLookup(i => i.ScanId);
        var links = (await db.ReceiptScanLinks.AsNoTracking().Where(l => ids.Contains(l.ScanId)).Select(l => new { l.ScanId, l.TransactionId }).ToListAsync(ct))
            .ToLookup(l => l.ScanId);
        return scans.Select(s => new
        {
            id = s.Id, amountCents = s.AmountCents, merchantName = s.MerchantName, transactedAt = s.TransactedAt, categorySlug = s.CategorySlug,
            needsReview = s.NeedsReview, createdAt = s.CreatedAt,
            items = items[s.Id].Select(i => new { position = i.Position, name = i.Name, amountCents = i.AmountCents, categorySlug = i.CategorySlug, parentPosition = i.ParentPosition }),
            transactionIds = links[s.Id].Select(l => l.TransactionId),
        }).ToList();
    }

    private async Task<object> AnomaliesAsync(Guid userId, CancellationToken ct)
        => await db.TransactionAnomalies.IgnoreQueryFilters().AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                transactionId = a.TransactionId, categoryId = a.CategoryId, kind = a.Kind, severity = a.Severity, amountCents = a.AmountCents,
                typicalCents = a.TypicalCents, ratioPercent = a.RatioPercent, resolution = a.Resolution, resolvedAt = a.ResolvedAt, createdAt = a.CreatedAt,
            }).ToListAsync(ct);

    private async Task<object> QrTransfersAsync(Guid userId, CancellationToken ct)
        => await db.QrTransferIntents.AsNoTracking().Where(q => q.UserId == userId).OrderBy(q => q.ScannedAt)
            .Select(q => new
            {
                receiverBankBin = q.ReceiverBankBin, receiverAccountNumber = q.ReceiverAccountNumber, receiverName = q.ReceiverName, amountCents = q.AmountCents,
                message = q.Message, categoryId = q.CategoryId, status = q.Status, scannedAt = q.ScannedAt, matchedTransactionId = q.MatchedTransactionId,
                matchedAt = q.MatchedAt,
            }).ToListAsync(ct);
}
