using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateManualTransactionCommandValidator : AbstractValidator<CreateManualTransactionCommand>
{
    public CreateManualTransactionCommandValidator()
    {
        RuleFor(x => x.FinancialAccountId).NotEmpty().WithMessage("Vui lòng chọn tài khoản.");
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.TransactionType).IsInEnum().WithMessage("Loại giao dịch không hợp lệ.");
        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
