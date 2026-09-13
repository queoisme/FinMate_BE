using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Gamification;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions.Commands;

public class DeleteTransactionCommandHandler : IDeleteTransactionCommandHandler
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly IBudgetPeriodService _budgetPeriodService;
    private readonly IGamificationService _gamificationService;
    private readonly ICacheService _cache;

    public DeleteTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IFinancialAccountRepository financialAccountRepository,
        IBudgetPeriodService budgetPeriodService,
        IGamificationService gamificationService,
        ICacheService cache)
    {
        _transactionRepository = transactionRepository;
        _financialAccountRepository = financialAccountRepository;
        _budgetPeriodService = budgetPeriodService;
        _gamificationService = gamificationService;
        _cache = cache;
    }

    public async Task HandleAsync(DeleteTransactionCommand command, CancellationToken ct = default)
    {
        var transaction = await _transactionRepository.GetByIdAsync(command.TransactionId, command.UserId, ct)
            ?? throw new NotFoundException("Transaction", command.TransactionId);

        var now = DateTimeOffset.UtcNow;
        var wasConfirmed = transaction.Status == TransactionStatus.Confirmed;

        if (wasConfirmed)
        {
            var account = await _financialAccountRepository.GetByIdAsync(transaction.FinancialAccountId, command.UserId, ct)
                ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
            account.BalanceCents -= transaction.TransactionType == TransactionType.Credit
                ? transaction.AmountCents
                : -transaction.AmountCents;
            account.UpdatedAt = now;

            // Transfer đã cộng tiền vào ví đích lúc tạo — không trả lại thì tiền tự sinh ra.
            if (transaction.CounterAccountId is not null)
            {
                var counter = await _financialAccountRepository.GetByIdAsync(transaction.CounterAccountId.Value, command.UserId, ct)
                    ?? throw new NotFoundException("FinancialAccount", transaction.CounterAccountId.Value);
                counter.BalanceCents -= transaction.AmountCents;
                counter.UpdatedAt = now;
            }

            // Xoá chỉ sinh delta ÂM nên không bao giờ chạm ngưỡng cảnh báo — bỏ qua giá trị
            // trả về là có chủ ý, không phải quên.
            await _budgetPeriodService.ApplyDeltaAsync(
                command.UserId,
                transaction.CategoryId,
                -TransactionBudgetDelta.Spend(transaction.TransactionType, transaction.AmountCents),
                transaction.TransactedAt,
                ct);

            // Trừ lại đúng số EXP đã cộng. Việc này CÓ THỂ làm tụt level, và đó là đánh đổi
            // có chủ ý: không hoàn EXP thì user farm được bằng cách thêm rồi xóa giao dịch
            // liên tục. Tiến độ mission không hoàn lại — mission đã hoàn thành là việc đã
            // xảy ra trong chu kỳ đó.
            //
            // Số hoàn phải tra theo Source vì 2 đường tạo thưởng 2 mức khác nhau: giao dịch
            // từ thông báo được +10 lúc confirm, giao dịch tự nhập (kể cả transfer) chỉ +5.
            await _gamificationService.RevertExpAsync(
                command.UserId, ExpAwardedFor(transaction.Source), ct);
        }

        transaction.DeletedAt = now;
        transaction.UpdatedAt = now;

        // account và budget period (nếu có) đã tracked cùng DbContext — UpdateAsync flush
        // atomically, xem ghi chú trong ConfirmTransactionCommandHandler.
        await _transactionRepository.UpdateAsync(transaction, ct);

        if (wasConfirmed)
        {
            await TransactionBudgetDelta.InvalidateSummaryAsync(_cache, command.UserId, transaction.TransactedAt, ct);
            await GamificationCache.InvalidateAsync(_cache, command.UserId, ct);
        }
    }

    private static int ExpAwardedFor(TransactionSource source) => source switch
    {
        TransactionSource.Notification => TransactionExpRewards.ConfirmTransaction,
        _ => TransactionExpRewards.CreateManualTransaction,
    };
}
