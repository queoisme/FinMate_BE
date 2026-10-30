using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

/// <summary>Một ngân hàng VietQR kèm việc FinMate có đọc được thông báo của nó không (<c>provider_key</c> có và provider đang bật).</summary>
public sealed record QrBankView(VietQrBank Bank, bool CanMatchNotifications);

/// <summary>
/// Dữ liệu của luồng quét QR chuyển khoản: danh mục ngân hàng (dùng chung), ý định chuyển tiền và bảng "người nhận → danh mục". Mọi hàm của hai nhóm sau đều lọc theo <c>userId</c>:
/// không có đường nào lấy một dòng chỉ bằng id.
/// </summary>
public interface IQrTransferRepository
{
    // ------------------------------------------------------------------ ngân hàng (dùng chung)
    Task<IReadOnlyList<QrBankView>> GetActiveBanksAsync(CancellationToken ct = default);

    /// <summary>
    /// BIN của ngân hàng sở hữu app có <paramref name="packageName"/>, đi qua <c>provider_configs.package_name</c> rồi <c>vietqr_banks.provider_key</c>. Null nếu app đó không phải ngân hàng VietQR.
    /// Đây là chỗ thay cho bảng package→BIN viết cứng của bản thử: thêm ngân hàng mới là thêm dòng, không sửa code.
    /// </summary>
    Task<string?> GetBankBinByPackageAsync(string packageName, CancellationToken ct = default);

    // ------------------------------------------------------------------ ý định chuyển tiền
    Task AddIntentAsync(QrTransferIntent intent, CancellationToken ct = default);
    Task<QrTransferIntent?> GetIntentAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<QrTransferIntent>> ListIntentsAsync(Guid userId, QrTransferStatus? status, int limit, CancellationToken ct = default);

    /// <summary>Ý định đang chờ gần nhất của người dùng: đúng ngân hàng gửi, đúng số tiền, quét trong <c>[notBefore, notAfter]</c>.</summary>
    Task<QrTransferIntent?> FindPendingAsync(
        Guid userId, string senderBankBin, long amountCents, DateTimeOffset notBefore, DateTimeOffset notAfter, CancellationToken ct = default);

    Task<QrTransferIntent?> GetIntentByTransactionAsync(Guid transactionId, Guid userId, CancellationToken ct = default);
    Task UpdateIntentAsync(QrTransferIntent intent, CancellationToken ct = default);

    // ------------------------------------------------------------------ người nhận → danh mục
    Task<CounterpartyCategoryOverride?> GetOverrideAsync(Guid userId, string bankBin, string accountNumber, CancellationToken ct = default);
    Task<CounterpartyCategoryOverride?> GetOverrideByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<CounterpartyCategoryOverride>> ListOverridesAsync(Guid userId, CancellationToken ct = default);
    Task AddOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default);
    Task UpdateOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default);
    Task RemoveOverrideAsync(CounterpartyCategoryOverride mapping, CancellationToken ct = default);
}
