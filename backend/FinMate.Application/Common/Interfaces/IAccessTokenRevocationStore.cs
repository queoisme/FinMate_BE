namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Đánh dấu "mọi access token của người này cấp đến giờ đều vô hiệu". Access token là JWT không
/// tra DB mỗi request, nên khóa tài khoản hay hạ quyền không đuổi được phiên đang cầm
/// (tối đa <c>JWT_ACCESS_TTL_MINUTES</c>); kho này đóng cửa sổ đó mà không thêm truy vấn DB.
///
/// Chỉ giữ mốc trong thời gian bằng TTL access token (sau đó mọi token cũ đã tự hết hạn).
/// Lỗi kho (Redis sập) không được làm sập request: kiểm tra thì coi như chưa thu hồi, đánh dấu
/// thì bỏ qua; DB vẫn là nguồn sự thật vì refresh token đã bị thu hồi riêng.
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>Vô hiệu mọi access token đã cấp cho <paramref name="userId"/> tính tới hết giây hiện tại.</summary>
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);

    /// <param name="issuedAtUnixSeconds">Claim <c>iat</c> của token; <c>null</c> nếu token không có.</param>
    Task<bool> IsRevokedAsync(Guid userId, long? issuedAtUnixSeconds, CancellationToken ct = default);
}
