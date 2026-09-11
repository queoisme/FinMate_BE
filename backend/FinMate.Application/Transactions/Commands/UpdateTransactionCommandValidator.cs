using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionCommandValidator()
    {
        RuleFor(x => x.FinancialAccountId).NotEmpty().WithMessage("Vui lòng chọn tài khoản.");
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.TransactionType).IsInEnum().WithMessage("Loại giao dịch không hợp lệ.");

        RuleFor(x => x.CounterAccountId)
            .NotEmpty()
            .When(x => x.TransactionType == TransactionType.Transfer)
            .WithMessage("Chuyển khoản nội bộ phải có ví đích.");

        RuleFor(x => x.CounterAccountId)
            .Empty()
            .When(x => x.TransactionType != TransactionType.Transfer)
            .WithMessage("Chỉ chuyển khoản nội bộ mới có ví đích.");

        RuleFor(x => x.CategoryId)
            .Empty()
            .When(x => x.TransactionType == TransactionType.Transfer)
            .WithMessage("Chuyển khoản nội bộ không thuộc danh mục chi tiêu nào.");
        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
