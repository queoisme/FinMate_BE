using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.QrTransfers;

/// <summary>
/// Dual-write 1 chiều CounterpartyCategoryOverride (QR, khóa BankBin+AccountNumber) ->
/// MerchantCategoryOverride (notification thường, khóa tên merchant đã chuẩn hoá) — §16.6/§16.9.
/// Dùng chung cho mọi nơi QR flow ghi/sửa override, để Flow 1 cũng thấy được lựa chọn này cho
/// lần sau cùng người nhận. KHÔNG làm chiều ngược lại (notification không có BankBin/AccountNumber
/// có cấu trúc để key). Lỗi ở đây không được làm hỏng việc ghi CounterpartyCategoryOverride đã
/// xong — chỉ log, không throw.
/// </summary>
internal static class CounterpartyMerchantSync
{
    public static async Task UpsertAsync(
        IMerchantCategoryOverrideRepository merchantCategoryOverrides,
        ILogger logger,
        Guid userId,
        string? displayName,
        Guid categoryId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        try
        {
            var key = MerchantCategoryKey.Normalize(displayName);
            var existing = await merchantCategoryOverrides.GetAsync(userId, key, ct);
            if (existing is null)
            {
                await merchantCategoryOverrides.AddAsync(new MerchantCategoryOverride
                {
                    UserId = userId,
                    MerchantKey = key,
                    DisplayName = displayName,
                    CategoryId = categoryId,
                    CreatedAt = now,
                    UpdatedAt = now,
                }, ct);
            }
            else
            {
                existing.CategoryId = categoryId;
                existing.UpdatedAt = now;
                await merchantCategoryOverrides.UpdateAsync(existing, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Dual-write MerchantCategoryOverride thất bại cho user {UserId}, receiver {ReceiverName} — CounterpartyCategoryOverride vẫn đã ghi, không rollback.",
                userId, displayName);
        }
    }
}
