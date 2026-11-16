using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Ghi tay một khoản hoàn (hoặc đảo) cho khoản chi <paramref name="OriginalTransactionId"/> (Phase 81, <c>POST /transactions/{id}/refunds</c>).
/// Mọi trường đều tuỳ chọn: mặc định hoàn phần còn lại, vào ví của khoản gốc, ngày hiện tại, loại <c>Refund</c>.
/// </summary>
public record CreateRefundCommand(
    Guid UserId,
    Guid OriginalTransactionId,
    long? AmountCents = null,
    DateTimeOffset? TransactedAt = null,
    Guid? FinancialAccountId = null,
    TransactionRelation? Relation = null,
    Guid? ClientRequestId = null);

public interface ICreateRefundCommandHandler
{
    Task<TransactionCreationResult> HandleAsync(CreateRefundCommand command, CancellationToken ct = default);
}
