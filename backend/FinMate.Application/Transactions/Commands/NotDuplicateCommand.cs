using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary><c>POST /transactions/{id}/not-duplicate</c> (Phase 82, BR-DUP-07): người dùng nói đây là hai khoản thật.</summary>
public record NotDuplicateCommand(Guid UserId, Guid TransactionId);

public interface INotDuplicateCommandHandler
{
    Task<TransactionDto> HandleAsync(NotDuplicateCommand command, CancellationToken ct = default);
}

/// <summary>
/// Đặt <c>Dismissed</c> và GIỮ liên kết: để không gợi ý lại cặp này và để phát hiện bất thường không gắn cờ <c>Duplicate</c> cho nó (BR-DUP-13).
/// Không đụng tiền. Gọi lại khi đã <c>Dismissed</c> → trả như cũ (200). Không có liên kết nào → 422. Khoá dòng như mọi thao tác đổi một giao
/// dịch có thể đang được gộp (D11), để lượt gộp chạy song song không ghi đè.
/// </summary>
public class NotDuplicateCommandHandler : INotDuplicateCommandHandler
{
    private readonly ITransactionRepository _transactions;
    private readonly IDuplicateDetector? _duplicates;

    public NotDuplicateCommandHandler(ITransactionRepository transactions, IDuplicateDetector? duplicates = null)
    {
        _transactions = transactions;
        _duplicates = duplicates;
    }

    public async Task<TransactionDto> HandleAsync(NotDuplicateCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactions.RunWithTransactionsLockedAsync(command.UserId, [command.TransactionId], async rows =>
        {
            var tx = rows[command.TransactionId];
            if (tx.DeletedAt is not null)
            {
                throw new NotFoundException("Transaction", command.TransactionId);
            }

            if (tx.DuplicateStatus is null)
            {
                throw new BusinessRuleException(TransactionErrorCodes.NotSuspectedDuplicate, "Giao dịch này không bị nghi trùng với giao dịch nào.");
            }

            if (tx.DuplicateStatus == DuplicateStatus.Suspected)
            {
                tx.DuplicateStatus = DuplicateStatus.Dismissed;
                tx.UpdatedAt = DateTimeOffset.UtcNow;
                await _transactions.UpdateAsync(tx, ct);
            }

            return tx;
        }, ct);

        var view = _duplicates is null ? null : await _duplicates.ViewAsync(command.UserId, [transaction], ct);
        return TransactionMapper.ToDto(transaction, duplicates: view?.GetValueOrDefault(transaction.Id));
    }
}
