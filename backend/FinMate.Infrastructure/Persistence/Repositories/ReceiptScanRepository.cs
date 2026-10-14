using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class ReceiptScanRepository : IReceiptScanRepository
{
    private readonly FinMateDbContext _context;

    public ReceiptScanRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ReceiptScan scan, CancellationToken ct = default)
    {
        _context.ReceiptScans.Add(scan);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddItemsAsync(Guid scanId, IReadOnlyList<ReceiptScanItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            item.ScanId = scanId;
        }

        _context.ReceiptScanItems.AddRange(items);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> LinkToTransactionAsync(
        Guid scanId, Guid userId, Guid transactionId, string groupCategorySlug, CancellationToken ct = default)
    {
        // Một câu duy nhất: điều kiện chủ sở hữu (lần quét VÀ giao dịch cùng của userId) nằm trong SELECT, còn ON CONFLICT DO NOTHING
        // nuốt cả vi phạm khóa chính (giao dịch đã nối) lẫn duy nhất (scan, nhóm) — kể cả khi hai request chạy song song.
        var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO receipt_scan_links (transaction_id, scan_id, group_category_slug)
            SELECT t.id, s.id, {groupCategorySlug}
            FROM receipt_scans s
            JOIN transactions t ON t.id = {transactionId} AND t.user_id = {userId}
            WHERE s.id = {scanId} AND s.user_id = {userId}
            ON CONFLICT DO NOTHING
            """, ct);
        return inserted == 1;
    }

    public Task<bool> IsSplitAsync(Guid scanId, Guid userId, CancellationToken ct = default)
        // Lọc theo chủ sở hữu: không đọc món của người khác.
        => IsAnySplitAsync(_context.ReceiptScans.Where(s => s.Id == scanId && s.UserId == userId), ct);

    public Task<bool> IsLinkedToSplitScanAsync(Guid transactionId, Guid userId, CancellationToken ct = default)
        => IsAnySplitAsync(_context.ReceiptScans.Where(s =>
            s.UserId == userId && _context.ReceiptScanLinks.Any(l => l.TransactionId == transactionId && l.ScanId == s.Id)), ct);

    /// <summary>
    /// Điều kiện "tách" dùng chung: lần quét được ghi nguồn "items" (đường tách ghi vậy, kể cả khi lưu món hỏng), hoặc các món đã lưu
    /// có từ 2 nhóm khác nhau (slug null = unknown), bỏ dòng giảm giá/phụ thu.
    /// </summary>
    private async Task<bool> IsAnySplitAsync(IQueryable<ReceiptScan> scans, CancellationToken ct)
    {
        if (await scans.AnyAsync(s => s.CategorySource == ReceiptScan.ItemsCategorySource, ct))
        {
            return true;
        }

        var groups = await _context.ReceiptScanItems.AsNoTracking()
            .Where(i => scans.Any(s => s.Id == i.ScanId))
            .Where(i => i.CategorySource != "discount" && i.CategorySource != "surcharge")
            .Select(i => i.CategorySlug ?? ReceiptScanLink.UnknownGroup)
            .Distinct()
            .Take(2)
            .CountAsync(ct);
        return groups >= 2;
    }

    public async Task<Guid?> GetLinkedScanIdAsync(Guid transactionId, Guid userId, CancellationToken ct = default)
    {
        return await _context.ReceiptScans.AsNoTracking()
            .Where(s => s.UserId == userId && _context.ReceiptScanLinks.Any(l => l.TransactionId == transactionId && l.ScanId == s.Id))
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ReceiptScan?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
        => _context.ReceiptScans.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
}
