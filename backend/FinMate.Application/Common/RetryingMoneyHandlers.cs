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
