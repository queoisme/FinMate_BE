using FluentValidation;

namespace FinMate.Application.Devices.Commands;

public class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenCommandValidator()
    {
        // Không kiểm định dạng token: Google không công bố cấu trúc và đã từng đổi độ dài.
        // Một regex đoán mò ở đây sẽ từ chối token hợp lệ trong tương lai, mà token sai thì
        // FCM tự báo lại và chúng ta xoá — vòng phản hồi đó đáng tin hơn.
        // Trần 1024 không phải con số tuỳ tiện: token nằm dưới một index UNIQUE, mà btree
        // của Postgres từ chối entry quá ~2704 byte. Không chặn ở đây thì một token quá khổ
        // trả về 500 từ tầng DB thay vì 400 từ tầng validate. Token FCM thật ~163 ký tự nên
        // 1024 vẫn rộng rãi gấp nhiều lần.
        RuleFor(c => c.Token)
            .NotEmpty().WithMessage("Token thiết bị không được để trống.")
            .MaximumLength(1024).WithMessage("Token thiết bị quá dài.");

        RuleFor(c => c.Platform).IsInEnum();
    }
}
