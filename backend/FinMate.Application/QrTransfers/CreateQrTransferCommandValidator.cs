using FluentValidation;

namespace FinMate.Application.QrTransfers;

public class CreateQrTransferCommandValidator : AbstractValidator<CreateQrTransferCommand>
{
    public CreateQrTransferCommandValidator()
    {
        RuleFor(c => c.ReceiverBankBin).Matches(@"^\d{6}$").WithMessage("Mã BIN ngân hàng nhận phải gồm 6 chữ số.");
        RuleFor(c => c.SenderBankBin).Matches(@"^\d{6}$").WithMessage("Mã BIN ngân hàng gửi phải gồm 6 chữ số.");

        // Số tài khoản có thể chứa chữ ở một số ngân hàng; chặn khoảng trắng và ký tự lạ để một chuỗi rác không vào DB rồi gây khó hiểu ở bước khớp.
        RuleFor(c => c.ReceiverAccountNumber).Matches(@"^[A-Za-z0-9]{4,32}$").WithMessage("Số tài khoản người nhận không hợp lệ.");

        // Trần số tiền và độ dài nội dung theo cấu hình hệ thống được kiểm ở QrTransferService; ở đây chỉ chặn những gì sai ở mọi cấu hình.
        RuleFor(c => c.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0.");

        // Nội dung chuyển khoản bị chuyển vào liên kết mở app ngân hàng nên giới hạn ngắn và không chứa ký tự điều khiển.
        // 100 là độ rộng cột DB, không phải quy tắc nghiệp vụ (giới hạn nghiệp vụ nằm ở cấu hình, tối đa 100).
        RuleFor(c => c.Message).MaximumLength(100).Must(NoControlChars).When(c => c.Message is not null).WithMessage("Nội dung chuyển khoản không hợp lệ.");
        RuleFor(c => c.ReceiverName).MaximumLength(100).Must(NoControlChars).When(c => c.ReceiverName is not null).WithMessage("Tên người nhận không hợp lệ.");
    }

    private static bool NoControlChars(string? value) => value is null || !value.Any(char.IsControl);
}
