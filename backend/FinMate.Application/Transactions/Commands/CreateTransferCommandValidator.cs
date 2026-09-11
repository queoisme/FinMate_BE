using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.FromAccountId).NotEmpty().WithMessage("Vui lòng chọn ví nguồn.");
        RuleFor(x => x.ToAccountId).NotEmpty().WithMessage("Vui lòng chọn ví đích.");
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
