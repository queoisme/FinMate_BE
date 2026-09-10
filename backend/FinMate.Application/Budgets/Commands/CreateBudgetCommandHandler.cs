using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

public class CreateBudgetCommandHandler : ICreateBudgetCommandHandler
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ICacheService _cache;
    private readonly IValidator<CreateBudgetCommand> _validator;

    public CreateBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        ICacheService cache,
        IValidator<CreateBudgetCommand> validator)
    {
        _budgetRepository = budgetRepository;
        _categoryRepository = categoryRepository;
        _transactionRepository = transactionRepository;
        _cache = cache;
        _validator = validator;
    }

    public async Task<BudgetDto> HandleAsync(CreateBudgetCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        Category? category = null;
        if (command.CategoryId is not null)
        {
            category = await _categoryRepository.GetByIdAsync(command.CategoryId.Value, ct);
            if (category is null || (category.UserId is not null && category.UserId != command.UserId))
            {
                throw new NotFoundException("Category", command.CategoryId.Value);
            }
        }

        if (await _budgetRepository.ExistsAsync(command.UserId, command.CategoryId, BudgetPeriodType.Monthly, ct))
        {
            throw new ConflictException(
                BudgetErrorCodes.AlreadyExists,
                command.CategoryId is null
                    ? "Bạn đã có hạn mức tổng cho chu kỳ này."
                    : "Bạn đã có hạn mức cho danh mục này.");
        }

        var now = DateTimeOffset.UtcNow;
        var (periodStart, periodEnd) = BudgetCalendar.MonthlyPeriod(now);

        var budget = new Budget
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            CategoryId = command.CategoryId,
            LimitCents = command.LimitCents,
            PeriodType = BudgetPeriodType.Monthly,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Budget tạo giữa tháng vẫn phải phản ánh phần đã tiêu từ đầu chu kỳ.
        var alreadySpent = await _transactionRepository.SumConfirmedSpendAsync(
            command.UserId, command.CategoryId, periodStart, periodEnd, ct);

        _budgetRepository.AddPeriod(new BudgetPeriod
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LimitCents = budget.LimitCents,
            SpentCents = alreadySpent,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _budgetRepository.AddAsync(budget, ct);
        budget.Category = category;

        var (year, month) = BudgetCalendar.VietnamYearMonth(now);
        await _cache.RemoveAsync(CacheKeys.BudgetSummary(command.UserId, year, month), ct);

        return BudgetMapper.ToDto(budget);
    }
}
