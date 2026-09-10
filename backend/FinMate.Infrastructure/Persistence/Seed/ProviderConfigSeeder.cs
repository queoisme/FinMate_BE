using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

public static class ProviderConfigSeeder
{
    private static readonly (string Key, string DisplayName, string PackageName, AccountType Type)[] Providers =
    {
        ("mb_bank", "MB Bank", "com.mbmobile", AccountType.Bank),
        ("vietcombank", "Vietcombank", "com.VCB", AccountType.Bank),
        ("momo", "MoMo", "com.mservice.momotransfer", AccountType.EWallet),
        ("zalopay", "ZaloPay", "vn.com.vng.zalopay", AccountType.EWallet),
        ("vnpay", "VNPay", "vn.vnpay.vnpayewallet", AccountType.EWallet),
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
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(ct);
    }
}
