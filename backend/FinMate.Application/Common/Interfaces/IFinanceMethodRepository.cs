using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Danh mục do admin quản lý: cách quản lý tài chính, mẫu mục tiêu và liên kết giữa chúng. Không xóa cứng thứ gì — tắt bằng <c>is_active</c>.
/// Đây là dữ liệu DÙNG CHUNG (không thuộc người dùng nào) nên các hàm không nhận <c>userId</c>.
/// </summary>
public interface IFinanceMethodRepository
{
    Task<List<FinanceMethod>> GetMethodsAsync(bool includeInactive, CancellationToken ct = default);
    Task<List<FinanceMethod>> GetMethodsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<FinanceMethod?> GetMethodByIdAsync(Guid id, CancellationToken ct = default);
    Task<FinanceMethod?> GetMethodByCodeAsync(string code, CancellationToken ct = default);
    Task AddMethodAsync(FinanceMethod method, CancellationToken ct = default);
    Task UpdateMethodAsync(FinanceMethod method, CancellationToken ct = default);

    Task<List<GoalTemplate>> GetTemplatesAsync(bool includeInactive, CancellationToken ct = default);
    Task<GoalTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);
    Task<GoalTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default);
    Task AddTemplateAsync(GoalTemplate template, CancellationToken ct = default);
    Task UpdateTemplateAsync(GoalTemplate template, CancellationToken ct = default);

    Task<List<MethodGoalTemplate>> GetLinksForMethodsAsync(IEnumerable<Guid> methodIds, CancellationToken ct = default);

    /// <summary>Thay TOÀN BỘ liên kết của một phương pháp bằng danh sách mới, trong một giao dịch.</summary>
    Task ReplaceLinksAsync(Guid methodId, IReadOnlyList<MethodGoalTemplate> links, CancellationToken ct = default);
}
