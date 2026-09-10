using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.FinancialAccounts.Commands;

public class CreateFinancialAccountCommandValidator : AbstractValidator<CreateFinancialAccountCommand>
{
    public CreateFinancialAccountCommandValidator()
    {
        RuleFor(x => x.AccountName)
            .NotEmpty().WithMessage("Tên tài khoản không được để trống.")
            .MaximumLength(100).WithMessage("Tên tài khoản tối đa 100 ký tự.");

        RuleFor(x => x.AccountType)
            .IsInEnum().WithMessage("Loại tài khoản không hợp lệ.");

        RuleFor(x => x.InitialBalanceCents)
            .GreaterThanOrEqualTo(0).WithMessage("Số dư ban đầu không được âm.");

        When(x => x.AccountType == AccountType.Cash, () =>
        {
            RuleFor(x => x.ProviderConfigId)
                .Must(id => id is null)
                .WithMessage("Tài khoản tiền mặt không cần chọn nhà cung cấp.");
        }).Otherwise(() =>
        {
            RuleFor(x => x.ProviderConfigId)
                .NotEmpty().WithMessage("Vui lòng chọn nhà cung cấp cho tài khoản này.");
        });
    }
}
