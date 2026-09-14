using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Gamification;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Ghi nhận chuyển khoản nội bộ giữa 2 ví của cùng user (Core Flow 2c).
/// Khác 3 handler tạo/sửa/xóa giao dịch thường ở đúng một điểm: nó chạm **2** số dư, và
/// KHÔNG gọi <see cref="IBudgetPeriodService"/> lần nào — tiền không rời khỏi túi user nên
/// không có hạn mức nào bị tiêu. Xem ARCHITECTURE.md §0 quyết định #1.
/// </summary>
public class CreateTransferCommandHandler : ICreateTransferCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IGamificationService _gamificationService;
    private readonly ICacheService _cache;
    private readonly IValidator<CreateTransferCommand> _validator;

    public CreateTransferCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IGamificationService gamificationService,
        ICacheService cache,
        IValidator<CreateTransferCommand> validator)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _gamificationService = gamificationService;
        _cache = cache;
        _validator = validator;
    }

    public async Task<TransactionCreationResult> HandleAsync(CreateTransferCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        // Gửi lại thì trả về chính giao dịch cũ — xem Transaction.ClientRequestId.
        if (command.ClientRequestId is { } clientRequestId)
        {
            var existing = await _transactionRepository.GetByClientRequestIdAsync(
                command.UserId, clientRequestId, ct);
            if (existing is not null)
            {
                return new TransactionCreationResult(TransactionMapper.ToDto(existing), AlreadyExisted: true);
            }
        }

        if (command.FromAccountId == command.ToAccountId)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.TransferSameAccount,
                "Ví nguồn và ví đích phải khác nhau.");
        }

        // Cả 2 lượt đọc đều đi qua userId — ví của user khác trả về NotFound chứ không lộ ra
        // là nó có tồn tại hay không (AGENTS.md §3: không bao giờ lấy resource chỉ bằng ID).
        var from = await _financialAccountRepository.GetByIdAsync(command.FromAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.FromAccountId);
        var to = await _financialAccountRepository.GetByIdAsync(command.ToAccountId, command.UserId, ct)
            ?? throw new NotFoundException("FinancialAccount", command.ToAccountId);

        var now = DateTimeOffset.UtcNow;

        from.BalanceCents -= command.AmountCents;
        from.UpdatedAt = now;
        to.BalanceCents += command.AmountCents;
        to.UpdatedAt = now;

        var transaction = new Transaction
        {
            UserId = command.UserId,
            ClientRequestId = command.ClientRequestId,
            FinancialAccountId = from.Id,
            CounterAccountId = to.Id,
            CategoryId = null,
            AmountCents = command.AmountCents,
            TransactionType = TransactionType.Transfer,
            Source = TransactionSource.Manual,
            Status = TransactionStatus.Confirmed,
            Description = command.Description,
            TransactedAt = command.TransactedAt,

            // Số dư ví NGUỒN sau giao dịch, thống nhất với debit/credit: cột này luôn nói về
            // financial_account_id, không phải về ví đích.
            BalanceAfterCents = from.BalanceCents,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _gamificationService.RecordActivityAsync(
            new GamificationActivity(
                command.UserId,
                MissionConditionType.CreateManualTransaction,
                TransactionExpRewards.CreateManualTransaction,
                now),
            ct);

        // 2 account và gamification đã tracked cùng DbContext — AddAsync flush atomically,
        // xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.AddAsync(transaction, ct);

        await GamificationCache.InvalidateAsync(_cache, command.UserId, ct);

        return new TransactionCreationResult(TransactionMapper.ToDto(transaction), AlreadyExisted: false);
    }
}
