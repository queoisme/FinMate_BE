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

        // VietinBank (iPay): package_name đã đối chiếu với thông báo thật trên thiết bị (định
        // dạng GD:/SDC:). Pattern trích xuất nằm ở kb_provider_formats của AI Service.
        ("vietinbank", "VietinBank", "com.vietinbank.ipay", AccountType.Bank, true),

        // Ba provider docx Bước 1.2 có nêu mà hệ thống còn thiếu. package_name dưới đây chưa
        // được kiểm chứng trên máy thật và cũng chưa có pattern trích xuất nào, nên chúng
        // nằm ở trạng thái tắt cho tới khi có người đối chiếu.
        ("techcombank", "Techcombank", "vn.com.techcombank.bb.app", AccountType.Bank, false),
        ("vpbank", "VPBank", "com.vnpay.vpbankonline", AccountType.Bank, false),
        ("shopeepay", "ShopeePay", "com.shopee.vn", AccountType.EWallet, false),

        // BIDV: package_name đã thấy trên điện thoại thật (adb shell pm list packages).
        ("bidv", "BIDV", "com.vnpay.bidv", AccountType.Bank, true),

        // Các ngân hàng / ví còn lại: package_name lấy từ nghiên cứu (Google Play + nguồn thứ hai,
        // 2026-10-30), CHƯA kiểm chứng trên máy thật nên để tắt. Bật bằng PATCH
        // /api/v1/admin/provider-configs/{id}/activation sau khi đối chiếu thông báo thật.
        // Đây là nguồn DUY NHẤT của danh sách (SeedAsync chạy mỗi lần BE khởi động, chỉ thêm dòng còn thiếu); không có bản SQL song song để khỏi lệch nhau.
        ("agribank", "Agribank", "com.vnpay.Agribank3g", AccountType.Bank, false),
        ("acb", "ACB", "mobile.acb.com.vn", AccountType.Bank, false),
        ("sacombank", "Sacombank", "com.sacombank.ewallet", AccountType.Bank, false),
        ("tpbank", "TPBank", "com.tpb.mb.gprsandroid", AccountType.Bank, false),
        ("vib", "VIB", "com.vib.myvib2", AccountType.Bank, false),
        ("msb", "MSB", "com.msb.digibank.retail", AccountType.Bank, false),
        ("ocb", "OCB", "vn.com.ocb.awe", AccountType.Bank, false),
        ("shb", "SHB", "vn.shb.saha.mbanking", AccountType.Bank, false),
        ("seabank", "SeABank", "vn.com.seabank.mb1", AccountType.Bank, false),
        ("lpbank", "LPBank", "vn.com.lpbank.digibank", AccountType.Bank, false),
        ("eximbank", "Eximbank", "com.vnpay.EximBankOmni", AccountType.Bank, false),
        ("namabank", "Nam A Bank", "ops.namabank.com.vn", AccountType.Bank, false),
        ("bacabank", "Bac A Bank", "com.bab.retailUAT", AccountType.Bank, false),
        ("abbank", "ABBank", "vn.abbank.retail", AccountType.Bank, false),
        ("ncb", "NCB", "com.ncb.bank", AccountType.Bank, false),
        ("pvcombank", "PVcomBank", "com.pvcombank.retail", AccountType.Bank, false),
        ("bvbank", "BVBank (Bản Việt)", "vn.banvietbank.mobilebanking", AccountType.Bank, false),
        ("cake", "Cake", "xyz.be.cake", AccountType.Bank, false),
        ("tnex", "TNEX", "vn.tnex.consumer", AccountType.Bank, false),
        ("timo", "Timo", "io.lifestyle.plus", AccountType.Bank, false),

        // Độ chắc chắn Thấp / Trung bình theo nghiên cứu (một nguồn, hoặc app đang chuyển đổi).
        ("hdbank", "HDBank (app cũ)", "com.vnpay.hdbank", AccountType.Bank, false),
        ("hdbank_di", "HDBank (Đi HDBank)", "com.hdbank.dihdbank", AccountType.Bank, false),
        ("vietbank", "Vietbank", "com.vn.vietbankomni", AccountType.Bank, false),
        ("baovietbank", "BAOVIET Bank", "com.vnpay.bvbank", AccountType.Bank, false),
        ("kienlongbank", "KienlongBank", "com.sunshine.ksbank", AccountType.Bank, false),

        ("viettelmoney", "Viettel Money", "com.bplus.vtpay", AccountType.EWallet, false),
        ("vnptmoney", "VNPT Money", "vnptpay.vnptmedia.vnpt.com.vnptpay", AccountType.EWallet, false),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existing = await context.ProviderConfigs
            .Select(p => new { p.ProviderKey, p.PackageName })
            .ToListAsync(ct);
        var existingKeys = existing.Select(p => p.ProviderKey).ToHashSet();
        var existingPackages = existing.Select(p => p.PackageName).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        foreach (var provider in Providers)
        {
            // package_name cũng có chỉ mục duy nhất: dòng admin đã thêm tay với cùng package
            // (khác provider_key) không được làm khởi động hỏng vì trùng khóa.
            if (existingKeys.Contains(provider.Key) || existingPackages.Contains(provider.PackageName))
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
