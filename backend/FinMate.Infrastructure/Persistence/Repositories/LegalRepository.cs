using FinMate.Application.Common.Exceptions;
using FinMate.Application.Legal;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class LegalRepository : ILegalRepository
{
    private readonly FinMateDbContext _context;

    public LegalRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<int?> GetCurrentVersionAsync(CancellationToken ct = default)
        => _context.LegalVersions.AsNoTracking()
            .Where(v => v.Status == LegalVersionStatus.Published)
            .MaxAsync(v => v.Version, ct);

    public Task<LegalVersion?> GetCurrentPublishedAsync(CancellationToken ct = default)
        => _context.LegalVersions.AsNoTracking()
            .Where(v => v.Status == LegalVersionStatus.Published)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

    public Task<LegalVersion?> GetDraftAsync(CancellationToken ct = default)
        => _context.LegalVersions.FirstOrDefaultAsync(v => v.Status == LegalVersionStatus.Draft, ct);

    public async Task<IReadOnlyList<LegalVersion>> ListAsync(CancellationToken ct = default)
        => await _context.LegalVersions.AsNoTracking().OrderByDescending(v => v.UpdatedAt).ToListAsync(ct);

    public void Add(LegalVersion version) => _context.LegalVersions.Add(version);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(LegalErrorCodes.DraftStale, "Bản nháp vừa được người khác thay đổi. Hãy tải lại rồi làm tiếp.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Hai người cùng tạo nháp đầu tiên: chỉ mục "một nháp" chặn người sau.
            throw new ConflictException(LegalErrorCodes.DraftStale, "Vừa có người khác tạo bản nháp. Hãy tải lại rồi làm tiếp.");
        }
    }
}
