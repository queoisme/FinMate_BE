using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Nạp danh mục ngân hàng VietQR: BIN NAPAS, tên và mã app theo <c>dl.vietqr.io</c> (tài liệu vietqr.io/deeplink, đối chiếu chéo với bảng BIN của NAPAS). Chạy mỗi lần khởi động và chỉ THÊM dòng còn thiếu
/// (không ghi đè): admin đổi tên, tắt, hay gắn <c>provider_key</c> thì bản đó thắng. Mã MB (<c>mb</c>) và VietinBank (<c>icb</c>) đã thử thật trên máy; các mã còn lại tra theo tài liệu, chưa thử tay.
///
/// <c>provider_key</c> trỏ tới <c>provider_configs</c> để biết ngân hàng nào FinMate đọc được thông báo (cần cho việc tự khớp giao dịch). Ngân hàng không có provider vẫn quét và mở app được, chỉ là
/// giao dịch sau đó không tự gắn người nhận.
/// </summary>
public static class VietQrBankSeeder
{
    private static readonly (string Bin, string Name, string AppCode, string? ProviderKey)[] Banks =
    {
        ("970415", "VietinBank", "icb", "vietinbank"),
        ("970422", "MB Bank", "mb", "mb_bank"),
        ("970418", "BIDV", "bidv", "bidv"),
        ("970436", "Vietcombank", "vcb", "vietcombank"),
        ("970407", "Techcombank", "tcb", "techcombank"),
        ("970432", "VPBank", "vpb", "vpbank"),
        ("970416", "ACB", "acb", "acb"),
        ("970423", "TPBank", "tpb", "tpbank"),
        ("970405", "Agribank", "vba", "agribank"),
        ("970441", "VIB", "vib", "vib"),
        ("970448", "OCB", "ocb", "ocb"),
        ("970443", "SHB", "shb", "shb"),
        ("970449", "LPBank", "lpb", "lpbank"),
        ("970440", "SeABank", "seab", "seabank"),
        ("970437", "HDBank", "hdb", "hdbank"),
        ("970431", "Eximbank", "eib", "eximbank"),
        ("970428", "Nam A Bank", "nab", "namabank"),
        ("970425", "ABBank", "abb", "abbank"),
        ("970419", "NCB", "ncb", "ncb"),
        ("970412", "PVcomBank", "pvcb", "pvcombank"),
        ("970452", "KienlongBank", "klb", "kienlongbank"),
        ("970438", "BaoViet Bank", "bvb", "baovietbank"),
        ("546034", "CAKE by VPBank", "cake", "cake"),
        ("963388", "Timo", "timo", "timo"),
        ("970429", "SCB", "scb", null),
        ("970424", "Shinhan Bank Việt Nam", "shbvn", null),
        ("970446", "Co-opBank", "coopbank", null),
        ("970457", "Woori Bank Việt Nam", "wvn", null),
        ("970427", "VietABank", "vab", null),
        ("970414", "OceanBank", "oceanbank", null),
        ("970439", "Public Bank Việt Nam", "pbvn", null),
        ("970400", "SaigonBank", "sgicb", null),
        ("422589", "CIMB Việt Nam", "cimb", null),
    };

    public static async Task SeedAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var existing = (await context.VietQrBanks.Select(b => b.Bin).ToListAsync(ct)).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        foreach (var bank in Banks)
        {
            if (existing.Contains(bank.Bin))
            {
                continue;
            }

            context.VietQrBanks.Add(new VietQrBank
            {
                Bin = bank.Bin, DisplayName = bank.Name, DeeplinkAppCode = bank.AppCode, ProviderKey = bank.ProviderKey, IsActive = true, CreatedAt = now, UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Các <c>provider_key</c> trong <c>vietqr_banks</c> mà <c>provider_configs</c> không có. Khớp thông báo đi qua hai bảng này bằng chuỗi (không có khóa ngoại), nên một khóa gõ sai hay bị xóa làm ngân hàng
    /// đó âm thầm không bao giờ tự khớp. <c>provider_key</c> của provider là bất biến nên đổi tên không gây ra chuyện này; lỗi chỉ đến từ dữ liệu sửa tay, vì vậy được kiểm và báo lúc khởi động.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindDanglingProviderKeysAsync(FinMateDbContext context, CancellationToken ct = default)
    {
        var known = (await context.ProviderConfigs.Select(p => p.ProviderKey).ToListAsync(ct)).ToHashSet();
        return (await context.VietQrBanks.Where(b => b.ProviderKey != null).Select(b => b.ProviderKey!).ToListAsync(ct))
            .Where(key => !known.Contains(key))
            .Distinct()
            .ToList();
    }
}
