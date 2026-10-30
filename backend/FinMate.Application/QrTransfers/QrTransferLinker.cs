using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.QrTransfers;

/// <summary>
/// Cầu nối giữa luồng quét QR và hai luồng giao dịch có sẵn (phân tích thông báo, sửa giao dịch). Tách riêng để hai handler đó chỉ phụ thuộc một giao diện nhỏ và vẫn chạy được khi không có nó.
/// </summary>
public interface IQrTransferLinker
{
    /// <summary>Ý định đang chờ khớp với một thông báo chi tiền, hoặc null. Chỉ giao dịch chi (Debit) mới có thể là kết quả của việc quét mã để chuyển đi.</summary>
    Task<QrTransferIntent?> FindMatchAsync(Guid userId, string packageName, DateTimeOffset receivedAt, long amountCents, CancellationToken ct = default);

    /// <summary>Đánh dấu ý định đã khớp với giao dịch nháp vừa tạo.</summary>
    Task LinkAsync(QrTransferIntent intent, Guid transactionId, CancellationToken ct = default);

    /// <summary>
    /// Người dùng vừa SỬA TAY danh mục của một giao dịch: nếu giao dịch đó đã khớp một lần quét QR thì ghi nhớ danh mục cho người nhận. Không làm ở bước xác nhận vì ở đó không phân biệt được
    /// "người dùng chủ động chọn" với "chỉ chấp nhận gợi ý của AI".
    /// </summary>
    Task LearnCategoryAsync(Guid userId, Guid transactionId, Guid categoryId, CancellationToken ct = default);
}

public class QrTransferLinker : IQrTransferLinker
{
    /// <summary>
    /// Thông báo phải đến trong vòng này SAU lúc quét. Đủ rộng cho quét → mở app ngân hàng → nhập OTP → xác nhận, đủ hẹp để không khớp nhầm với giao dịch khác cùng số tiền. Chưa có yêu cầu nghiệp vụ ấn
    /// định con số khác.
    /// </summary>
    public static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Đồng hồ điện thoại (gắn vào thông báo) và đồng hồ máy chủ (gắn vào lúc quét) có thể lệch nhau. Cho phép lần quét "xảy ra sau" thông báo tối đa chừng này; hơn thế thì lần quét chắc chắn không phải
    /// nguyên nhân (bản thử trước nhận cả lần quét sau thông báo tới 30 phút).
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private readonly IQrTransferRepository _repository;

    public QrTransferLinker(IQrTransferRepository repository)
    {
        _repository = repository;
    }

    public async Task<QrTransferIntent?> FindMatchAsync(Guid userId, string packageName, DateTimeOffset receivedAt, long amountCents, CancellationToken ct = default)
    {
        // Thông báo đến từ app của ngân hàng GỬI: so với ngân hàng gửi người dùng đã chọn, không phải ngân hàng nhận.
        var senderBin = await _repository.GetBankBinByPackageAsync(packageName, ct);
        if (senderBin is null)
        {
            return null;
        }

        return await _repository.FindPendingAsync(userId, senderBin, amountCents, receivedAt - MatchWindow, receivedAt + ClockSkew, ct);
    }

    public async Task LinkAsync(QrTransferIntent intent, Guid transactionId, CancellationToken ct = default)
    {
        intent.Status = QrTransferStatus.Matched;
        intent.MatchedTransactionId = transactionId;
        intent.MatchedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateIntentAsync(intent, ct);
    }

    public async Task LearnCategoryAsync(Guid userId, Guid transactionId, Guid categoryId, CancellationToken ct = default)
    {
        var intent = await _repository.GetIntentByTransactionAsync(transactionId, userId, ct);
        if (intent is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _repository.GetOverrideAsync(userId, intent.ReceiverBankBin, intent.ReceiverAccountNumber, ct);
        if (existing is null)
        {
            await _repository.AddOverrideAsync(new CounterpartyCategoryOverride
            {
                UserId = userId,
                BankBin = intent.ReceiverBankBin,
                AccountNumber = intent.ReceiverAccountNumber,
                DisplayName = intent.ReceiverName,
                CategoryId = categoryId,
                CreatedAt = now,
                UpdatedAt = now,
            }, ct);
            return;
        }

        existing.CategoryId = categoryId;
        existing.UpdatedAt = now;
        await _repository.UpdateOverrideAsync(existing, ct);
    }
}
