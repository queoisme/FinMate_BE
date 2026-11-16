using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary><c>POST /transactions/{id}/not-transfer</c> (Phase 83, BR-TRF-12): "đây không phải tiền chuyển giữa các ví của tôi".</summary>
public record NotTransferCommand(Guid UserId, Guid TransactionId);

public interface INotTransferCommandHandler
{
    Task<TransactionDto> HandleAsync(NotTransferCommand command, CancellationToken ct = default);
}

/// <summary>
/// Đặt <c>transfer_dismissed_at</c> cho giao dịch này VÀ cho chiều đang được gợi ý ghép cùng (D7): bác một chiều mà chiều kia vẫn mang gợi ý
/// "nạp ví" thì người dùng vừa bác xong lại bị hỏi. Chỉ tắt gợi ý; chuyển đổi tay vẫn được (S2). Gọi lại → như cũ (200). Không đụng tiền.
/// </summary>
public class NotTransferCommandHandler : INotTransferCommandHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly ITransferSuggestions _suggestions;

    public NotTransferCommandHandler(ITransactionRepository transactions, ITransferSuggestions suggestions)
    {
        _transactions = transactions;
        _suggestions = suggestions;
    }

    public async Task<TransactionDto> HandleAsync(NotTransferCommand command, CancellationToken ct = default)
    {
        var current = await _transactions.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);
        var paired = (await _suggestions.ForOneAsync(command.UserId, current, ct))?.PairedId;
        var ids = paired is { } p ? new[] { current.Id, p } : [current.Id];

        var transaction = await _transactions.RunWithTransactionsLockedAsync(command.UserId, ids, async rows =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows.Values.Where(r => r.DeletedAt is null && r.TransferDismissedAt is null
                && r.TransactionType is TransactionType.Debit or TransactionType.Credit))
            {
                row.TransferDismissedAt = now;
                row.UpdatedAt = now;
            }

            var tx = rows[command.TransactionId];
            if (tx.DeletedAt is not null)
            {
                throw new NotFoundException("Transaction", command.TransactionId);
            }

            await _transactions.UpdateAsync(tx, ct);
            return tx;
        }, ct);

        return TransactionMapper.ToDto(transaction);
    }
}
