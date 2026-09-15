namespace FinMate.Application.Common.Interfaces;

public interface IGoogleCodeExchanger
{
    /// <summary>
    /// Đổi <c>code</c> Google trả về ở callback lấy <c>id_token</c>. Null khi Google từ chối.
    ///
    /// Chỉ lấy đúng <c>id_token</c> và bỏ phần còn lại: access token của Google dùng để gọi
    /// API của Google, mà hệ thống này không gọi cái nào — giữ lại là giữ một thứ có quyền mà
    /// không có việc dùng đến.
    /// </summary>
    Task<string?> ExchangeForIdTokenAsync(string code, CancellationToken ct = default);
}
