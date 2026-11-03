using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Budgets.Commands;

public record TransferBudgetLimitCommand(Guid UserId, Guid FromBudgetId, Guid ToBudgetId, long AmountCents);

public record BudgetTransferResultDto(BudgetDto From, BudgetDto To);

public interface ITransferBudgetLimitCommandHandler
{
    Task<BudgetTransferResultDto> HandleAsync(TransferBudgetLimitCommand command, CancellationToken ct = default);
}

public class TransferBudgetLimitCommandValidator : AbstractValidator<TransferBudgetLimitCommand>
{
    public TransferBudgetLimitCommandValidator()
    {
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền chuyển phải lớn hơn 0.");
        RuleFor(x => x).Must(x => x.FromBudgetId != x.ToBudgetId).WithName(nameof(TransferBudgetLimitCommand.ToBudgetId))
            .WithMessage("Ngân sách nguồn và đích phải khác nhau.");
    }
}
