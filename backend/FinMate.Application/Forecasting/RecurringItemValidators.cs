using FluentValidation;

namespace FinMate.Application.Forecasting;

internal static class RecurringItemRules
{
    /// <summary>Trần số tiền một khoản định kỳ (đồng): chặn số rác, không phải hạn mức nghiệp vụ.</summary>
    public const long MaxAmountCents = 100_000_000_000;

    /// <summary>Ngày đến hạn được đặt lệch hôm nay tối đa chừng này ngày.</summary>
    public const int MaxDueDateOffsetDays = 366;

    public static bool DueDateInRange(DateOnly date)
    {
        var today = Common.VietnamTime.Today();
        return date >= today.AddDays(-MaxDueDateOffsetDays) && date <= today.AddDays(MaxDueDateOffsetDays);
    }
}

public class CreateRecurringItemCommandValidator : AbstractValidator<CreateRecurringItemCommand>
{
    public CreateRecurringItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên khoản định kỳ không được để trống.")
            .MaximumLength(RecurringItemLimits.NameMaxLength).WithMessage($"Tên khoản định kỳ tối đa {RecurringItemLimits.NameMaxLength} ký tự.");
        RuleFor(x => x.CounterpartyName).MaximumLength(200).WithMessage("Tên người nhận tối đa 200 ký tự.");
        RuleFor(x => x.AmountCents).InclusiveBetween(1, RecurringItemRules.MaxAmountCents).WithMessage("Số tiền phải lớn hơn 0.");
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Cadence).IsInEnum();
        RuleFor(x => x.NextDueDate).Must(RecurringItemRules.DueDateInRange).WithMessage("Ngày đến hạn phải trong vòng một năm quanh hôm nay.");
    }
}

public class UpdateRecurringItemCommandValidator : AbstractValidator<UpdateRecurringItemCommand>
{
    public UpdateRecurringItemCommandValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().WithMessage("Tên khoản định kỳ không được để trống.")
            .MaximumLength(RecurringItemLimits.NameMaxLength).WithMessage($"Tên khoản định kỳ tối đa {RecurringItemLimits.NameMaxLength} ký tự.")
            .When(x => x.Name is not null);
        RuleFor(x => x.AmountCents!.Value).InclusiveBetween(1, RecurringItemRules.MaxAmountCents).WithMessage("Số tiền phải lớn hơn 0.")
            .When(x => x.AmountCents is not null);
        RuleFor(x => x.Cadence!.Value).IsInEnum().When(x => x.Cadence is not null);
        RuleFor(x => x.NextDueDate!.Value).Must(RecurringItemRules.DueDateInRange).WithMessage("Ngày đến hạn phải trong vòng một năm quanh hôm nay.")
            .When(x => x.NextDueDate is not null);
    }
}
