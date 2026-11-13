using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.Transactions.Commands;

namespace FinMate.Application.Common;

// Lớp bọc thay vì sửa từng handler: logic tiền giữ nguyên, và mọi đường gọi qua interface (controller, chat) đều được bảo vệ.

public sealed class RetryingCreateManualTransactionCommandHandler(ICreateManualTransactionCommandHandler inner, IConcurrencyRetry retry)
    : ICreateManualTransactionCommandHandler
{
    public Task<TransactionCreationResult> HandleAsync(CreateManualTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingConfirmTransactionCommandHandler(IConfirmTransactionCommandHandler inner, IConcurrencyRetry retry)
    : IConfirmTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(ConfirmTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingCreateRefundCommandHandler(ICreateRefundCommandHandler inner, IConcurrencyRetry retry)
    : ICreateRefundCommandHandler
{
    public Task<TransactionCreationResult> HandleAsync(CreateRefundCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingMergeTransactionsCommandHandler(IMergeTransactionsCommandHandler inner, IConcurrencyRetry retry)
    : IMergeTransactionsCommandHandler
{
    public Task<TransactionDto> HandleAsync(MergeTransactionsCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingLinkRelatedTransactionCommandHandler(ILinkRelatedTransactionCommandHandler inner, IConcurrencyRetry retry)
    : ILinkRelatedTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(LinkRelatedTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingUnlinkRelatedTransactionCommandHandler(IUnlinkRelatedTransactionCommandHandler inner, IConcurrencyRetry retry)
    : IUnlinkRelatedTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(UnlinkRelatedTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingUpdateTransactionCommandHandler(IUpdateTransactionCommandHandler inner, IConcurrencyRetry retry)
    : IUpdateTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(UpdateTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingDeleteTransactionCommandHandler(IDeleteTransactionCommandHandler inner, IConcurrencyRetry retry)
    : IDeleteTransactionCommandHandler
{
    public Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(
            async token =>
            {
                await inner.HandleAsync(command, token);
                return true;
            },
            ct);
}

public sealed class RetryingCreateTransferCommandHandler(ICreateTransferCommandHandler inner, IConcurrencyRetry retry)
    : ICreateTransferCommandHandler
{
    public Task<TransactionCreationResult> HandleAsync(CreateTransferCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class RetryingContributeToGoalCommandHandler(IContributeToGoalCommandHandler inner, IConcurrencyRetry retry)
    : IContributeToGoalCommandHandler
{
    public Task<SavingGoalDto> HandleAsync(ContributeToGoalCommand command, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.HandleAsync(command, token), ct);
}

// ---- Phase 82 (D11): khoá dòng giao dịch trước khi đổi nó ----
// transactions không có xmin. Lượt gộp xoá mềm bản bị gộp dưới FOR UPDATE; nếu xác nhận/sửa/xoá cùng dòng chạy song song mà không khoá, lệnh
// UPDATE của nó (không đụng deleted_at) có thể để lại một dòng vừa bị gộp vừa đã xác nhận, ví đã bị trừ. Bọc ngoài handler (dưới lớp Retrying),
// nên mỗi lần thử lại là một DB transaction mới và handler giữ nguyên. Thứ tự khoá: dòng giao dịch → (khoản gốc của Phase 81) → ví → kỳ ngân sách.

public sealed class RowLockedConfirmTransactionCommandHandler(IConfirmTransactionCommandHandler inner, ITransactionRepository transactions)
    : IConfirmTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(ConfirmTransactionCommand command, CancellationToken ct = default)
        => transactions.RunWithTransactionsLockedAsync(command.UserId, [command.TransactionId], _ => inner.HandleAsync(command, ct), ct);
}

public sealed class RowLockedUpdateTransactionCommandHandler(IUpdateTransactionCommandHandler inner, ITransactionRepository transactions)
    : IUpdateTransactionCommandHandler
{
    public Task<TransactionDto> HandleAsync(UpdateTransactionCommand command, CancellationToken ct = default)
        => transactions.RunWithTransactionsLockedAsync(command.UserId, [command.TransactionId], _ => inner.HandleAsync(command, ct), ct);
}

public sealed class RowLockedDeleteTransactionCommandHandler(IDeleteTransactionCommandHandler inner, ITransactionRepository transactions)
    : IDeleteTransactionCommandHandler
{
    public Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
        => transactions.RunWithTransactionsLockedAsync(command.UserId, [command.TransactionId], async _ =>
        {
            await inner.HandleAsync(command, ct);
            return true;
        }, ct);
}
