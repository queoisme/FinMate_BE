namespace FinMate.API.Configuration;

/// <summary>
/// Đọc <c>OPS_WEB_ORIGINS</c>: các domain của web vận hành được phép gọi API từ trình duyệt. App Flutter không bị CORS nên không cần khai báo.
/// Cấu hình sai thì không cho khởi động, vì để lọt <c>*</c> thì mọi trang web đều gọi được API bằng token của nhân viên đang đăng nhập.
/// </summary>
public static class OpsWebOrigins
{
    public const string PolicyName = "OpsWeb";

    public static IReadOnlyList<string> Parse(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return [];
        }

        return configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(string origin)
    {
        var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.PathAndQuery == "/"
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo)
            && !origin.Contains('*');

        if (!valid)
        {
            throw new InvalidOperationException(
                $"OPS_WEB_ORIGINS có giá trị không hợp lệ: '{origin}'. Cần dạng https://ops.example.com, không có đường dẫn và không dùng ký tự *.");
        }

        return uri!.GetLeftPart(UriPartial.Authority);
    }
}
