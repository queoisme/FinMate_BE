using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public interface ISystemConfigRepository
{
    Task<SystemConfigVersion?> GetActiveAsync(string name, CancellationToken ct = default);

    Task<SystemConfigVersion?> GetVersionAsync(string name, int version, CancellationToken ct = default);

    /// <summary>Lịch sử, mới nhất trước.</summary>
    Task<(List<SystemConfigVersion> Items, int Total)> ListAsync(string name, int limit, int offset, CancellationToken ct = default);

    /// <summary>
    /// Lưu trữ phiên bản đang chạy và thêm <paramref name="next"/> làm phiên bản đang chạy, trong MỘT giao dịch. Chỉ thành công khi phiên bản đang chạy đúng là
    /// <paramref name="expectedVersion"/> (0 = chưa có gì): người sửa nhìn thấy một bản cũ thì trả <c>false</c> thay vì ghi đè thay đổi của người khác.
    /// Gán <see cref="SystemConfigVersion.Version"/> cho <paramref name="next"/> (số kế tiếp).
    /// </summary>
    Task<bool> TryActivateNextAsync(SystemConfigVersion next, int expectedVersion, CancellationToken ct = default);

    // ---- Phase 95: bản nháp có phạm vi (Scope). Lịch sử và runtime không thấy chúng.

    Task AddDraftAsync(SystemConfigVersion draft, CancellationToken ct = default);

    /// <summary>Bản nháp theo id (có theo dõi để sửa). Null nếu không có hoặc không phải bản nháp có phạm vi.</summary>
    Task<SystemConfigVersion?> GetDraftAsync(string name, Guid id, CancellationToken ct = default);

    Task<SystemConfigVersion?> GetPendingAsync(string name, string scope, CancellationToken ct = default);

    /// <summary>Bản nháp của một phạm vi (mọi trạng thái, kể cả đã duyệt), mới tạo trước.</summary>
    Task<(List<SystemConfigVersion> Items, int Total)> ListDraftsAsync(string name, string scope, SystemConfigStatus? status, int offset, int limit, CancellationToken ct = default);

    /// <summary>Lưu thay đổi của bản nháp. Trả <c>false</c> khi phạm phải chỉ mục "một bản chờ duyệt" (người khác vừa gửi duyệt).</summary>
    Task<bool> TrySaveDraftChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Duyệt: trong MỘT giao dịch, nếu phiên bản đang chạy vẫn là <c>draft.BaseVersion</c> thì lưu trữ nó, rồi biến <paramref name="draft"/> thành
    /// phiên bản đang chạy kế tiếp với nội dung <paramref name="fullContent"/>. Trả <c>false</c> khi bản nháp đã cũ (không tự gộp) hoặc người khác vừa lưu.
    /// </summary>
    Task<bool> TryActivateDraftAsync(SystemConfigVersion draft, string fullContent, CancellationToken ct = default);
}
