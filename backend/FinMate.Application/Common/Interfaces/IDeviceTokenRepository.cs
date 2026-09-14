using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

public interface IDeviceTokenRepository
{
    Task<IReadOnlyList<DeviceToken>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Tìm theo chính token. Đây là NGOẠI LỆ có chủ ý của luật "repository luôn lọc theo
    /// userId": token là thứ client vừa nhận từ FCM, chưa gắn với ai cả, và việc cần làm là
    /// tìm xem nó đang thuộc về ai để chuyển chủ. Lọc theo userId ở đây sẽ không bao giờ tìm
    /// thấy dòng của chủ cũ — đúng cái ca cần xử lý.
    /// </summary>
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken ct = default);

    Task AddAsync(DeviceToken deviceToken, CancellationToken ct = default);
    Task UpdateAsync(DeviceToken deviceToken, CancellationToken ct = default);

    /// <summary>Xoá hẳn. Token chết không có giá trị lịch sử nào để giữ lại.</summary>
    Task RemoveAsync(Guid userId, string token, CancellationToken ct = default);

    /// <summary>Dọn các token FCM đã báo là không còn tồn tại.</summary>
    Task RemoveManyAsync(IReadOnlyList<string> tokens, CancellationToken ct = default);
}
