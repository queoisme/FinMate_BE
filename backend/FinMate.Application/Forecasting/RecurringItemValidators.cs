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
        RuleFor(x => x.Installment!).SetValidator(new InstallmentFieldsValidator()).When(x => x.Installment is not null);
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
        RuleFor(x => x.Installment!).SetValidator(new InstallmentFieldsValidator()).When(x => x.Installment is not null);
        RuleFor(x => x.ClearEnd).Must((x, clear) => !clear || (x.Installment?.TotalOccurrences is null && x.Installment?.EndDate is null))
            .WithName("clearEnd").WithMessage("Không gửi bỏ kỳ cuối cùng với số kỳ hoặc ngày kết thúc.");
        RuleFor(x => x.Status).Must(s => s is null or Domain.Enums.RecurringStatus.Active)
            .WithName("status").WithMessage("Chỉ đổi được trạng thái sang Active (mở lại khoản đã tất toán).");
    }
}

/// <summary>Phạm vi của trường kỳ cuối (Phase 92, BR-INS-01, D18). Kiểm chéo cần trạng thái khoản nằm ở <see cref="InstallmentRules"/>.</summary>
public class InstallmentFieldsValidator : AbstractValidator<InstallmentFields>
{
    public const int MaxTotalOccurrences = 600;
    public const int MaxEndDateYears = 50;

    public InstallmentFieldsValidator()
    {
        RuleFor(x => x).Must(x => x.TotalOccurrences is null || x.EndDate is null)
            .WithName("totalOccurrences").WithMessage("Chỉ đặt một trong số kỳ hoặc ngày kết thúc.");
        RuleFor(x => x.TotalOccurrences!.Value).InclusiveBetween(1, MaxTotalOccurrences)
            .WithName("totalOccurrences").WithMessage($"Số kỳ phải từ 1 đến {MaxTotalOccurrences}.").When(x => x.TotalOccurrences is not null);
        RuleFor(x => x.EndDate!.Value).Must(d => d <= Common.VietnamTime.Today().AddYears(MaxEndDateYears))
            .WithName("endDate").WithMessage($"Ngày kết thúc tối đa {MaxEndDateYears} năm sau hôm nay.").When(x => x.EndDate is not null);
        RuleFor(x => x.AnchorDay!.Value).InclusiveBetween(1, 31)
            .WithName("anchorDay").WithMessage("Ngày trong tháng phải từ 1 đến 31.").When(x => x.AnchorDay is not null);
        RuleFor(x => x.PrepaidOccurrences!.Value).InclusiveBetween(0, MaxTotalOccurrences)
            .WithName("prepaidOccurrences").WithMessage($"Số kỳ đã trả trước phải từ 0 đến {MaxTotalOccurrences}.").When(x => x.PrepaidOccurrences is not null);
    }
}

/// <summary>Áp và kiểm chéo trường kỳ cuối lên một khoản (Phase 92). Lỗi là 400 như lỗi kiểm hợp lệ thường.</summary>
internal static class InstallmentRules
{
    /// <param name="nextDueChanged">Ngày đến hạn hoặc chu kỳ vừa đổi: anchor mặc định theo ngày đến hạn mới (BR-INS-05), trừ khi gửi tường minh.</param>
    public static void Apply(Domain.Entities.RecurringItem item, InstallmentFields? fields, bool clearEnd, bool nextDueChanged)
    {
        if (clearEnd)
        {
            item.TotalOccurrences = null;
            item.EndDate = null;
        }

        if (fields?.TotalOccurrences is { } total)
        {
            item.TotalOccurrences = total;
            item.EndDate = null;
        }

        if (fields?.EndDate is { } end)
        {
            item.EndDate = end;
            item.TotalOccurrences = null;
        }

        item.PrepaidOccurrences = fields?.PrepaidOccurrences ?? item.PrepaidOccurrences;
        item.AnchorDay = AnchorOf(item, fields?.AnchorDay, nextDueChanged);
    }

    private static int? AnchorOf(Domain.Entities.RecurringItem item, int? requested, bool nextDueChanged)
    {
        if (item.Cadence != Domain.Enums.RecurringCadence.Monthly)
        {
            if (requested is not null)
            {
                throw Invalid("anchorDay", "Ngày trong tháng chỉ dùng cho khoản hằng tháng.");
            }

            return null;
        }

        return requested ?? (nextDueChanged || item.AnchorDay is null ? item.NextDueDate.Day : item.AnchorDay);
    }

    /// <summary>D18: khi tạo, số kỳ đã trả trước phải nhỏ hơn tổng (không thì không có gì để theo dõi) và ngày kết thúc không trước ngày đến hạn.</summary>
    public static void EnsureCreatable(Domain.Entities.RecurringItem item, InstallmentFields? fields)
    {
        if (item.TotalOccurrences is { } total && item.PrepaidOccurrences >= total)
        {
            throw Invalid("prepaidOccurrences", "Số kỳ đã trả trước phải nhỏ hơn tổng số kỳ.");
        }

        EnsureEndAfterNextDue(item, fields);
    }

    public static void EnsureEndAfterNextDue(Domain.Entities.RecurringItem item, InstallmentFields? fields)
    {
        if (fields?.EndDate is { } end && end < item.NextDueDate)
        {
            throw Invalid("endDate", "Ngày kết thúc không được trước ngày đến hạn kế tiếp.");
        }
    }

    private static ValidationException Invalid(string field, string message)
        => new([new FluentValidation.Results.ValidationFailure(field, message)]);
}
