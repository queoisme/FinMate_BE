using FinMate.Domain.Entities;

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
}
