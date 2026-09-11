using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

public class CreateManualTransactionCommandValidator : AbstractValidator<CreateManualTransactionCommand>
{
    public CreateManualTransactionCommandValidator()
    {
        RuleFor(x => x.FinancialAccountId).NotEmpty().WithMessage("Vui lòng chọn tài khoản.");
        RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.TransactionType).IsInEnum().WithMessage("Loại giao dịch không hợp lệ.");

        // Endpoint này không có trường ví đích nên không dựng nổi một transfer hợp lệ; để lọt
        // xuống DB sẽ vi phạm chk_transactions_transfer_shape và trả 500 thay vì lỗi đọc được.
        RuleFor(x => x.TransactionType)
            .NotEqual(TransactionType.Transfer)
            .WithMessage("Chuyển khoản nội bộ dùng POST /transactions/transfer.");
        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
