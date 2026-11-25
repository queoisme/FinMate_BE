using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Yêu cầu tải dữ liệu cá nhân (Phase 91). Mọi phương thức theo user đều lọc <c>userId</c>.</summary>
public interface IPersonalDataExportRepository
{
    Task<bool> HasActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Thời điểm yêu cầu của các yêu cầu tính vào hạn mức (D14) từ <paramref name="since"/>, cũ nhất trước.</summary>
    Task<IReadOnlyList<DateTimeOffset>> CountedRequestTimesSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Lưu yêu cầu mới. False khi user đã có một yêu cầu đang chạy (chỉ mục duy nhất một phần chặn, kể cả khi hai request tới cùng lúc).</summary>
    Task<bool> TryAddAsync(PersonalDataExport export, CancellationToken ct = default);

    Task<PersonalDataExport?> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Mới nhất trước, trong cửa sổ lịch sử.</summary>
    Task<IReadOnlyList<PersonalDataExport>> ListAsync(Guid userId, DateTimeOffset since, CancellationToken ct = default);
}
