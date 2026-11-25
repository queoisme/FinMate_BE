namespace FinMate.Application.DataExport.Package;

/// <summary>
/// Một file JSON của gói. <see cref="Data"/> là dữ liệu đã chiếu từng trường một (không bao giờ là entity), để một cột mới thêm vào bảng sau này
/// không tự lọt vào file người dùng tải về. <see cref="IsList"/> quyết định bọc dưới <c>items</c> (danh sách) hay <c>data</c> (một đối tượng).
/// </summary>
public sealed record PackageSection(string FileName, object Data, bool IsList);

/// <summary>Đọc toàn bộ dữ liệu cá nhân của MỘT user theo danh mục đã duyệt (kế hoạch Phase 91 mục 2), trừ giao dịch (đọc theo luồng riêng).</summary>
public interface IPersonalDataReader
{
    Task<IReadOnlyList<PackageSection>> ReadSectionsAsync(Guid userId, CancellationToken ct = default);
}
