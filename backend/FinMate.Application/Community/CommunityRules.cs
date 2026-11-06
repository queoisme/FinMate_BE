using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Exceptions;
using FinMate.Domain.Entities;

namespace FinMate.Application.Community;

public static class CommunityRules
{
    /// <summary>Đủ đồng ý và không bị cấm đăng. Đọc từ DB mỗi lần vì việc cấm phải có hiệu lực ngay.</summary>
    public static void EnsureCanPost(User user)
    {
        EnsureNotMuted(user);
        if (!user.CommunityConsentGranted)
        {
            throw new BusinessRuleException(CommunityErrorCodes.ConsentRequired, "Bạn cần đồng ý quy định cộng đồng trước khi đăng.");
        }
    }

    public static void EnsureNotMuted(User user)
    {
        if (user.CommunityMutedUntil > DateTimeOffset.UtcNow)
        {
            throw new BusinessRuleException(CommunityErrorCodes.Muted, "Tài khoản của bạn đang bị tạm cấm đăng trong cộng đồng.");
        }
    }

    /// <summary>Tên sắp hiện công khai nên phải sạch thông tin liên hệ. Ẩn danh thì không xét vì tên không hiện.</summary>
    public static void EnsureDisplayNameAllowed(bool isAnonymous, User user)
    {
        if (!isAnonymous && !CommunityContentPolicy.IsDisplayNameAllowed(user.DisplayName))
        {
            throw new BusinessRuleException(CommunityErrorCodes.DisplayNameNotAllowed, "Tên hiển thị của bạn có thông tin liên hệ nên chưa thể hiện công khai. Hãy đổi tên hoặc đăng ẩn danh.");
        }
    }

    /// <summary>Khu vực phải có trong cấu hình. Trả về chuỗi rỗng cho khu vực khi chỉ biết tới cấp tỉnh.</summary>
    public static (string Province, string Area) RequireRegion(string? provinceCode, string? areaCode, CommunitySettings settings)
    {
        var province = settings.Regions.FirstOrDefault(r => r.Code == provinceCode)
            ?? throw new BusinessRuleException(CommunityErrorCodes.RegionUnknown, "Tỉnh, thành không tồn tại.");
        if (string.IsNullOrEmpty(areaCode))
        {
            return (province.Code, string.Empty);
        }

        return province.Areas.Any(a => a.Code == areaCode)
            ? (province.Code, areaCode)
            : throw new BusinessRuleException(CommunityErrorCodes.RegionUnknown, "Khu vực không thuộc tỉnh, thành đã chọn.");
    }
}
