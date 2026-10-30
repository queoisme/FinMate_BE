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
    private readonly IQrTransferRepository _repository;
    private readonly ISystemConfigProvider _config;

    public QrTransferLinker(IQrTransferRepository repository, ISystemConfigProvider config)
    {
        _repository = repository;
        _config = config;
    }

    public async Task<QrTransferIntent?> FindMatchAsync(Guid userId, string packageName, DateTimeOffset receivedAt, long amountCents, CancellationToken ct = default)
    {
        // Thông báo đến từ app của ngân hàng GỬI: so với ngân hàng gửi người dùng đã chọn, không phải ngân hàng nhận.
        var senderBin = await _repository.GetBankBinByPackageAsync(packageName, ct);
        if (senderBin is null)
        {
            return null;
        }

        // Cửa sổ khớp và độ lệch đồng hồ do admin cấu hình (cấu hình hệ thống), không viết cứng.
        var settings = (await _config.GetAsync(ct)).Document.QrTransfer;
        return await _repository.FindPendingAsync(
            userId, senderBin, amountCents,
            receivedAt - TimeSpan.FromMinutes(settings.MatchWindowMinutes),
            receivedAt + TimeSpan.FromMinutes(settings.ClockSkewMinutes), ct);
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
