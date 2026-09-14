using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FinMate.API.Configuration;

/// <summary>
/// Dựng <see cref="ForwardedHeadersOptions"/> từ biến <c>TRUSTED_PROXIES</c>.
///
/// Đứng sau reverse proxy thì <c>RemoteIpAddress</c> là IP của PROXY, không phải của người
/// dùng — mà rate limiter phân vùng theo IP khi chưa đăng nhập, nên cả hệ thống sẽ dùng chung
/// một ngăn 10 lần/phút ở màn đăng nhập.
///
/// Bẫy: gọi <c>UseForwardedHeaders</c> trần thì ASP.NET chỉ tin proxy loopback, nên sau một
/// proxy thật nó LẶNG LẼ không làm gì. Còn tin mọi header là để client tự bịa IP và thoát rate
/// limit. Nên phải khai báo tin ai, và không khai thì không bật.
/// </summary>
public static class TrustedProxies
{
    public const string TrustAll = "*";

    /// <summary>Null nghĩa là KHÔNG bật — mặc định an toàn khi biến để trống.</summary>
    public static ForwardedHeadersOptions? BuildOptions(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };

        // Danh sách mặc định chỉ có loopback; xoá rồi mới nạp danh sách thật.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        if (configured.Trim() == TrustAll)
        {
            // Tin mọi forwarder. Một số nền tảng (Render, Fly, Cloud Run) bắt buộc phải thế vì
            // IP proxy của họ không cố định.
            //
            // Cách nói "tin tất cả" với middleware này là một dải bao trùm — để trống hai danh
            // sách lại có nghĩa NGƯỢC LẠI: không proxy nào được tin, và header bị bỏ qua lặng lẽ.
            //
            // ĐÁNH ĐỔI: client tự đặt được X-Forwarded-For, nên hạn mức theo IP không còn chặn
            // nổi kẻ cố tình vượt. Chỉ dùng khi nền tảng ĐÃ tự ghi đè header đó ở biên.
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Any, 0));
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.IPv6Any, 0));
            return options;
        }

        foreach (var proxy in configured.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }

            // Giá trị không parse được thì bỏ qua trong im lặng có chủ ý: một IP gõ sai không
            // đáng để cả dịch vụ không khởi động được, và hệ quả là an toàn hơn chứ không
            // nguy hơn — proxy đó đơn giản là không được tin.
        }

        return options;
    }
}
