using FinMate.Domain.Enums;
using FinMate.Application.Common;
using FluentValidation;

namespace FinMate.Application.Groups.Funds;

/// <param name="MemberUserId">Null khi người đó đã bị ẩn danh; khi đó <paramref name="MemberName"/> là "Thành viên cũ".</param>
/// <param name="HasPersonalLink">Chỉ có giá trị khi người xem là chủ dòng (D15): nhóm không thấy ví hay sổ cá nhân của nhau.</param>
public sealed record GroupFundEntryDto(
    Guid Id,
    Guid FundId,
    GroupFundEntryKind Kind,
    long AmountCents,
    DateTimeOffset TransactedAt,
    Guid? MemberUserId,
    string MemberName,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? CategoryName,
    string? Note,
    string? MerchantName,
    GroupFundEntryOrigin Origin,
    bool? HasPersonalLink,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="AlreadyExisted">Gửi lại cùng <c>clientRequestId</c>: trả dòng cũ, API trả 200 thay vì 201 (D19).</param>
public sealed record GroupFundEntryResult(GroupFundEntryDto Entry, GroupFundDto Fund, bool AlreadyExisted);

public sealed record GroupFundEntryListResult(IReadOnlyList<GroupFundEntryDto> Items, string? NextCursor);

/// <param name="FinancialAccountId">Ví cá nhân của người ghi (góp, rút, chi): sinh một giao dịch cá nhân gắn quỹ (BR-FUND-07, S4b).</param>
/// <param name="FromTransactionId">Chỉ với chi: chuyển một khoản chi đã có của người ghi thành khoản chi từ quỹ (S4a).</param>
public sealed record CreateGroupFundEntryCommand(
    Guid UserId,
    Guid GroupId,
    Guid FundId,
    GroupFundEntryKind Kind,
    long AmountCents,
    DateTimeOffset TransactedAt,
    Guid? CategoryId = null,
    Guid? GroupCategoryId = null,
    string? Note = null,
    string? MerchantName = null,
    Guid? FinancialAccountId = null,
    Guid? FromTransactionId = null,
    Guid? ClientRequestId = null);

public class CreateGroupFundEntryCommandValidator : AbstractValidator<CreateGroupFundEntryCommand>
{
    public const int NoteMaxLength = 500;
    public const int MerchantMaxLength = 200;

    public CreateGroupFundEntryCommandValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Loại dòng quỹ không hợp lệ.");
        RuleFor(x => x.TransactedAt).NotEmpty().WithMessage("Vui lòng chọn thời điểm.");
        RuleFor(x => x.Note).MaximumLength(NoteMaxLength).WithMessage($"Ghi chú tối đa {NoteMaxLength} ký tự.");
        RuleFor(x => x.MerchantName).MaximumLength(MerchantMaxLength).WithMessage($"Nơi chi tối đa {MerchantMaxLength} ký tự.");

        // Dòng tạo từ một giao dịch lấy số tiền của chính giao dịch đó (D23), nên không bắt buộc gửi số tiền.
        When(x => x.Kind != GroupFundEntryKind.Adjustment && x.FromTransactionId is null, () =>
            RuleFor(x => x.AmountCents).GreaterThan(0).WithMessage("Số tiền phải lớn hơn 0."));
        When(x => x.Kind == GroupFundEntryKind.Adjustment, () =>
        {
            RuleFor(x => x.AmountCents).NotEqual(0).WithMessage("Số tiền điều chỉnh phải khác 0.");
            RuleFor(x => x.Note).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Điều chỉnh quỹ phải có ghi chú.");
        });
        RuleFor(x => x.AmountCents).GreaterThanOrEqualTo(0).When(x => x.FromTransactionId is not null).WithMessage("Số tiền phải lớn hơn 0.");
        // Dòng điều chỉnh có thể âm nên giới hạn theo giá trị tuyệt đối.
        RuleFor(x => x.AmountCents).Must(a => a >= -MoneyLimits.MaxAmountCents && a <= MoneyLimits.MaxAmountCents).WithMessage(MoneyLimits.TooLargeMessage);
    }
}

public sealed record UpdateGroupFundEntryCommand(
    Guid UserId,
    Guid GroupId,
    Guid FundId,
    Guid EntryId,
    long? AmountCents = null,
    DateTimeOffset? TransactedAt = null,
    Guid? CategoryId = null,
    Guid? GroupCategoryId = null,
    string? Note = null,
    string? MerchantName = null,
    Guid? FinancialAccountId = null,
    bool RemoveFinancialAccount = false);
