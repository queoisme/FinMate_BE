using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Groups.Funds;

public partial class GroupFundEntryService
{
    /// <summary>
    /// D23: dòng lấy số tiền và thời điểm của giao dịch (sự thật ngân hàng), danh mục gốc để khôi phục, nơi chi và ghi chú nếu người ghi không gửi.
    /// Danh mục của dòng: cái người ghi chọn, không thì danh mục của giao dịch nếu là danh mục chi chung của hệ thống.
    /// </summary>
    private static void TakeFromSource(GroupFundEntry entry, Transaction source, CreateGroupFundEntryCommand command)
    {
        if (command.AmountCents != 0 && command.AmountCents != source.AmountCents)
        {
            throw Invalid("Số tiền của khoản chi từ quỹ phải bằng số tiền của giao dịch.");
        }

        entry.AmountCents = source.AmountCents;
        entry.TransactedAt = source.TransactedAt;
        entry.Origin = GroupFundEntryOrigin.Converted;
        entry.OriginCategoryId = source.CategoryId;
        entry.MerchantName ??= source.MerchantName;
        entry.Note ??= source.Description;
        if (entry.CategoryId is null && entry.GroupCategoryId is null && IsSharedSpendingCategory(source.Category))
        {
            entry.CategoryId = source.CategoryId;
        }

        if (entry.CategoryId is null && entry.GroupCategoryId is null)
        {
            throw Invalid("Hãy chọn danh mục cho khoản chi từ quỹ: danh mục của giao dịch này là danh mục riêng của bạn.");
        }
    }

    /// <summary>
    /// D24, S3: chủ dòng đưa giao dịch về lại khoản chi sau khi người quản lý đã xoá dòng quỹ (dòng đã xoá mềm, gốc là chuyển đổi). Không cần cờ quỹ:
    /// đây là sổ cá nhân của chính họ, cũng như xoá giao dịch gắn quỹ khi cờ tắt (D8).
    /// </summary>
    public async Task<TransactionDto> RestoreFromFundAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var (restored, effects) = await _transactions.RunWithTransactionsLockedAsync(userId, [transactionId], async rows =>
        {
            var personal = rows[transactionId];
            var entry = personal is { DeletedAt: null, GroupFundEntryId: { } entryId } ? await _funds.GetEntryByIdAsync(entryId, ct) : null;
            if (entry is not { DeletedAt: not null, Origin: GroupFundEntryOrigin.Converted })
            {
                throw new BusinessRuleException(
                    TransactionErrorCodes.GroupFundLinked, "Chỉ khôi phục được khoản chi lấy từ quỹ khi dòng quỹ đó đã bị xoá.");
            }

            var result = await _converter.RestoreAsync(userId, personal, entry, ct);
            await _groups.SaveChangesAsync(ct);
            return (personal, result);
        }, ct);

        await _converter.AfterSaveAsync(userId, effects, ct);
        return TransactionMapper.ToDto(restored);
    }

    private static bool IsSharedSpendingCategory(Category? category)
        => category is { IsSystem: true } && !LoanExclusion.IsLoan(category) && category.Slug != IncomeSlug;
}
