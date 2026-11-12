using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IEconomicAssumptionRepository
{
    Task<EconomicAssumption?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Phiên bản <see cref="AssumptionStatus.Approved"/> mới nhất của khóa; null nếu chưa có.</summary>
    Task<EconomicAssumption?> GetApprovedAsync(string key, CancellationToken ct = default);

    /// <summary>Các khóa đang có ít nhất một bản <see cref="AssumptionStatus.Approved"/>, xếp theo tên. Planner cần biết khóa nào tồn tại để tra đúng.</summary>
    Task<IReadOnlyList<string>> ListApprovedKeysAsync(CancellationToken ct = default);

    Task<int> GetMaxVersionAsync(string key, CancellationToken ct = default);
    Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default);
    Task AddAsync(EconomicAssumption assumption, CancellationToken ct = default);

    /// <summary>Lưu mọi thay đổi trên các bản ghi đã nạp (dùng khi duyệt: bản mới Approved, bản cũ Retired).</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
