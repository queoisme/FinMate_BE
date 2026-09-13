using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

public static class ProviderConfigSeeder
{
    /// <param name="IsActive">
    /// Provider seed ở trạng thái TẮT là provider có <c>package_name</c> CHƯA được đối chiếu
    /// với một thông báo thật. Sai <c>package_name</c> là hỏng im lặng: provider hiện ra ở
    /// onboarding, người dùng chọn nó, rồi không thông báo nào khớp cả và không có gì báo lỗi.
    /// Tắt sẵn thì nó không lọt vào danh sách người dùng thấy; admin bật lên qua
    /// <c>PATCH /api/v1/admin/provider-configs/{id}/activation</c> sau khi kiểm chứng.
    /// </param>
    private static readonly (string Key, string DisplayName, string PackageName, AccountType Type, bool IsActive)[] Providers =
    {
        // Năm provider đã có pattern trích xuất thật trong AI Service (provider_patterns).
        ("mb_bank", "MB Bank", "com.mbmobile", AccountType.Bank, true),
        ("vietcombank", "Vietcombank", "com.VCB", AccountType.Bank, true),
        ("momo", "MoMo", "com.mservice.momotransfer", AccountType.EWallet, true),
        ("zalopay", "ZaloPay", "vn.com.vng.zalopay", AccountType.EWallet, true),
        ("vnpay", "VNPay", "vn.vnpay.vnpayewallet", AccountType.EWallet, true),

        // Ba provider docx Bước 1.2 có nêu mà hệ thống còn thiếu. package_name dưới đây chưa
        // được kiểm chứng trên máy thật và cũng chưa có pattern trích xuất nào, nên chúng
        // nằm ở trạng thái tắt cho tới khi có người đối chiếu.
        ("techcombank", "Techcombank", "vn.com.techcombank.bb.app", AccountType.Bank, false),
        ("vpbank", "VPBank", "com.vnpay.vpbankonline", AccountType.Bank, false),
        ("shopeepay", "ShopeePay", "com.shopee.vn", AccountType.EWallet, false),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existingKeys = await context.ProviderConfigs
            .Select(p => p.ProviderKey)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var provider in Providers)
        {
            if (existingKeys.Contains(provider.Key))
            {
                continue;
            }

            context.ProviderConfigs.Add(new ProviderConfig
            {
                ProviderKey = provider.Key,
                DisplayName = provider.DisplayName,
                PackageName = provider.PackageName,
                AccountType = provider.Type,
                IsActive = provider.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(ct);
    }
}
