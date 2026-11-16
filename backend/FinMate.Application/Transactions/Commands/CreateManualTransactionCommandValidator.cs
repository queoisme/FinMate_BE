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
        RuleFor(x => x.Source)
            .IsInEnum().WithMessage("Kênh nhập không hợp lệ.");

        // Client KHÔNG được tự khai một giao dịch là do AI phát hiện. Source=Notification là
        // điều kiện lọc của tỉ lệ "người dùng sửa lại danh mục AI đoán" ở /admin/ai-stats;
        // cho phép khai bừa thì thước đo chất lượng của AI bị bóp méo bởi dữ liệu người dùng
        // tự nhập, và không ai nhìn ra vì con số vẫn trông hợp lý.
        RuleFor(x => x.Source)
            .NotEqual(TransactionSource.Notification)
            .WithMessage("Giao dịch từ thông báo chỉ được tạo qua POST /notifications/analyze.");

        // Giao dịch hỗ trợ tạo hộ chỉ đi qua API vận hành, có kiểm tra khách đã đồng ý.
        RuleFor(x => x.Source)
            .NotEqual(TransactionSource.Support)
            .WithMessage("Giao dịch do hỗ trợ tạo hộ không tạo được ở đây.");

        // Phase 81, D5: hoàn "mồ côi" (không có khoản gốc trong app). Có khoản gốc thì dùng POST /transactions/{id}/refunds.
        When(x => x.Relation is not null, () =>
        {
            RuleFor(x => x.Relation).Equal(TransactionRelation.Refund)
                .WithMessage("Chỉ ghi được hoàn tiền không có khoản gốc ở đây; đảo giao dịch dùng POST /transactions/{id}/refunds.");
            RuleFor(x => x.TransactionType).Equal(TransactionType.Credit).WithMessage("Khoản hoàn là tiền vào.");
            RuleFor(x => x.CategoryId).NotNull().WithMessage("Khoản hoàn không có khoản gốc phải chọn danh mục chi tiêu.");
            RuleFor(x => x.GroupId).Null().WithMessage("Khoản hoàn không có khoản gốc không chia sẻ vào nhóm được.");
        });

        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
