using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

/// <summary>
/// Sơ đồ trang 6: "Chuyển hạn mức từ danh mục khác". Chỉ chuyển phần CHƯA CHI của ngân sách nguồn trong kỳ hiện tại — chuyển nhiều hơn là
/// đẩy nguồn vào trạng thái vượt bằng một thao tác hành chính. Cả hai hạn mức (ngân sách và kỳ đang chạy) đổi trong cùng một lượt lưu.
/// Hạ hạn mức nguồn có thể chạm ngưỡng cảnh báo: <c>BudgetAlertJob</c> là lưới vét cho đúng trường hợp này.
/// </summary>
public class TransferBudgetLimitCommandHandler : ITransferBudgetLimitCommandHandler
{
    private readonly IBudgetRepository _budgets;
    private readonly ICacheService _cache;
    private readonly IValidator<TransferBudgetLimitCommand> _validator;
    private readonly IAlertThresholdProvider _thresholds;

    public TransferBudgetLimitCommandHandler(
        IBudgetRepository budgets, ICacheService cache, IValidator<TransferBudgetLimitCommand> validator, IAlertThresholdProvider thresholds)
    {
        _budgets = budgets;
        _cache = cache;
        _validator = validator;
        _thresholds = thresholds;
    }

    public async Task<BudgetTransferResultDto> HandleAsync(TransferBudgetLimitCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var from = await _budgets.GetByIdAsync(command.FromBudgetId, command.UserId, ct) ?? throw new NotFoundException("Budget", command.FromBudgetId);
        var to = await _budgets.GetByIdAsync(command.ToBudgetId, command.UserId, ct) ?? throw new NotFoundException("Budget", command.ToBudgetId);
        if (from.CategoryId is null || to.CategoryId is null)
        {
            throw new BusinessRuleException(BudgetErrorCodes.TransferNeedsCategoryBudgets, "Chỉ chuyển hạn mức giữa hai ngân sách theo danh mục.");
        }

        var now = DateTimeOffset.UtcNow;
        var (periodStart, _) = VietnamTime.MonthRange(now);
        var fromPeriod = await _budgets.GetPeriodAsync(from.Id, periodStart, ct);
        var toPeriod = await _budgets.GetPeriodAsync(to.Id, periodStart, ct);

        var unspent = (fromPeriod?.LimitCents ?? from.LimitCents) - (fromPeriod?.SpentCents ?? 0);
        if (command.AmountCents > unspent)
        {
            throw new BusinessRuleException(
                BudgetErrorCodes.TransferExceedsUnspent, "Chỉ chuyển được phần hạn mức chưa chi của ngân sách nguồn trong tháng này.");
        }

        var thresholds = await _thresholds.GetAsync(command.UserId, ct);
        Shift(from, fromPeriod, -command.AmountCents, now, thresholds);
        Shift(to, toPeriod, command.AmountCents, now, thresholds);

        await _budgets.UpdateAsync(from, ct);
        await _budgets.UpdateAsync(to, ct);

        var (year, month) = VietnamTime.YearMonthOf(now);
        await _cache.RemoveAsync(CacheKeys.BudgetSummary(command.UserId, year, month), ct);
        return new BudgetTransferResultDto(BudgetMapper.ToDto(from), BudgetMapper.ToDto(to));
    }

    private static void Shift(Budget budget, BudgetPeriod? period, long delta, DateTimeOffset now, AlertThresholds thresholds)
    {
        budget.LimitCents += delta;
        budget.UpdatedAt = now;
        if (period is null)
        {
            return;
        }

        period.LimitCents += delta;
        period.UpdatedAt = now;

        // Nâng hạn mức đủ để xuống dưới ngưỡng thì mở lại cờ cảnh báo, như khi sửa hạn mức trực tiếp.
        BudgetAlertEvaluator.ReopenBelow(period, thresholds);
    }
}
