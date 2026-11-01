using System.Text.RegularExpressions;
using FluentValidation;

namespace FinMate.Application.Admin.SystemConfig;

public class SystemConfigDocumentValidator : AbstractValidator<SystemConfigDocument>
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly Regex SemVersion = new(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.Compiled);

    // Tên font Google Fonts gồm chữ, số và dấu cách; không cần danh sách cố định vì app tự rơi về font mặc định khi không tải được.
    private static readonly Regex FontName = new("^[A-Za-z0-9 ]{1,40}$", RegexOptions.Compiled);

    public SystemConfigDocumentValidator()
    {
        RuleFor(x => x.Maintenance).NotNull().SetValidator(new MaintenanceValidator());
        RuleFor(x => x.AppVersion).NotNull().SetValidator(new AppVersionValidator());
        RuleFor(x => x.Features).NotNull();
        RuleFor(x => x.Limits).NotNull().SetValidator(new LimitValidator());
        RuleFor(x => x.Sync).NotNull().SetValidator(new SyncValidator());
        RuleFor(x => x.Theme).NotNull().SetValidator(new ThemeValidator());
        RuleFor(x => x.QrTransfer).NotNull().SetValidator(new QrTransferValidator());
        RuleFor(x => x.FinanceMethods).NotNull().SetValidator(new FinanceMethodValidator());
    }

    private static bool IsHttps(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private sealed class MaintenanceValidator : AbstractValidator<MaintenanceSettings>
    {
        public MaintenanceValidator()
        {
            RuleFor(x => x.Title).NotEmpty().WithMessage("Tiêu đề bảo trì không được để trống.")
                .MaximumLength(80).WithMessage("Tiêu đề bảo trì tối đa 80 ký tự.");
            RuleFor(x => x.Message).NotEmpty().WithMessage("Nội dung bảo trì không được để trống.")
                .MaximumLength(500).WithMessage("Nội dung bảo trì tối đa 500 ký tự.");
        }
    }

    private sealed class AppVersionValidator : AbstractValidator<AppVersionSettings>
    {
        public AppVersionValidator()
        {
            RuleFor(x => x.MinVersion).Matches(SemVersion).WithMessage("Phiên bản tối thiểu phải dạng số.số.số (vd 1.2.0).");
            RuleFor(x => x.LatestVersion).Matches(SemVersion).WithMessage("Phiên bản mới nhất phải dạng số.số.số (vd 1.2.0).");
            RuleFor(x => x).Must(x => !SemVersion.IsMatch(x.MinVersion) || !SemVersion.IsMatch(x.LatestVersion)
                    || Version.Parse(x.MinVersion) <= Version.Parse(x.LatestVersion))
                .WithName(nameof(AppVersionSettings.MinVersion)).WithMessage("Phiên bản tối thiểu không được lớn hơn phiên bản mới nhất.");
            RuleFor(x => x.StoreUrl).Must(url => url.Length == 0 || (url.Length <= 500 && IsHttps(url)))
                .WithMessage("Liên kết cửa hàng phải để trống hoặc là địa chỉ https (tối đa 500 ký tự).");
        }
    }

    private sealed class LimitValidator : AbstractValidator<LimitSettings>
    {
        public LimitValidator()
        {
            RuleFor(x => x.AlertT1Percent).InclusiveBetween(1, 98).WithMessage("Ngưỡng nhắc nhẹ (T1) phải từ 1 đến 98%.");
            RuleFor(x => x.AlertT2Percent).InclusiveBetween(2, 99).WithMessage("Ngưỡng cảnh báo mạnh (T2) phải từ 2 đến 99%; mức vượt hạn mức luôn là 100%.");
            RuleFor(x => x).Must(x => x.AlertT1Percent < x.AlertT2Percent)
                .WithName(nameof(LimitSettings.AlertT1Percent)).WithMessage("Ngưỡng nhắc nhẹ (T1) phải nhỏ hơn ngưỡng cảnh báo mạnh (T2).");
        }
    }

    private sealed class SyncValidator : AbstractValidator<SyncSettings>
    {
        public SyncValidator()
        {
            RuleFor(x => x.IntervalMinutes).InclusiveBetween(5, 1440).WithMessage("Chu kỳ đồng bộ phải từ 5 đến 1440 phút.");
            RuleFor(x => x.MaxRetries).InclusiveBetween(0, 20).WithMessage("Số lần thử lại phải từ 0 đến 20.");
            RuleFor(x => x.QueueRetentionDays).InclusiveBetween(1, 90).WithMessage("Thời gian giữ hàng đợi phải từ 1 đến 90 ngày.");
        }
    }

    private sealed class FinanceMethodValidator : AbstractValidator<FinanceMethodSettings>
    {
        public FinanceMethodValidator()
        {
            RuleFor(x => x.RecommendationReuseMinutes).InclusiveBetween(0, 1440).WithMessage("Thời gian dùng lại gợi ý phải từ 0 đến 1440 phút (0 = luôn tạo mới).");
            RuleFor(x => x.SupportMinSharePercent).InclusiveBetween(1, 100).WithMessage("Tỷ lệ điểm tối thiểu của cách bổ trợ phải từ 1 đến 100%.");
        }
    }

    private sealed class QrTransferValidator : AbstractValidator<QrTransferSettings>
    {
        // Chỉ tên miền (có dấu chấm, không cổng, không đường dẫn, không IP trần): thứ này quyết định số tài khoản của người dùng được gửi tới đâu.
        private static readonly Regex Host = new(@"^(?=.{4,100}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}$", RegexOptions.Compiled);

        public QrTransferValidator()
        {
            RuleFor(x => x.MatchWindowMinutes).InclusiveBetween(5, 180).WithMessage("Cửa sổ khớp phải từ 5 đến 180 phút.");
            RuleFor(x => x.ClockSkewMinutes).InclusiveBetween(0, 10).WithMessage("Độ lệch đồng hồ cho phép phải từ 0 đến 10 phút.");
            RuleFor(x => x.PendingLifetimeHours).InclusiveBetween(1, 168).WithMessage("Thời gian hết hạn phải từ 1 đến 168 giờ.");
            RuleFor(x => x.MaxAmount).InclusiveBetween(1_000, 1_000_000_000_000).WithMessage("Trần số tiền phải từ 1.000 đến 1.000 tỷ đồng.");
            RuleFor(x => x.MaxMessageLength).InclusiveBetween(20, 100).WithMessage("Độ dài nội dung chuyển khoản phải từ 20 đến 100 ký tự.");
            RuleFor(x => x.DefaultMessage).NotEmpty().WithMessage("Nội dung chuyển khoản mặc định không được để trống.")
                .Must(m => !m.Any(char.IsControl)).WithMessage("Nội dung chuyển khoản mặc định không được chứa ký tự điều khiển.");
            RuleFor(x => x).Must(x => x.DefaultMessage.Length <= x.MaxMessageLength)
                .WithName(nameof(QrTransferSettings.DefaultMessage)).WithMessage("Nội dung mặc định dài hơn độ dài tối đa cho phép.");
            RuleFor(x => x.DeeplinkHost).Must(h => Host.IsMatch(h)).WithMessage("Máy chủ chuyển tiếp phải là tên miền (vd dl.vietqr.io), không kèm https://, cổng hay đường dẫn.");
            RuleFor(x => x.DeeplinkReturnUrl).Must(url => url.Length <= 500 && IsHttps(url)).WithMessage("Liên kết quay về phải là địa chỉ https (tối đa 500 ký tự).");
        }
    }

    private sealed class ThemeValidator : AbstractValidator<ThemeSettings>
    {
        public ThemeValidator()
        {
            RuleFor(x => x.PrimaryColor).Must(BeColorOrEmpty).WithMessage("Màu chủ đạo phải là mã HEX 6 số (vd #2E7D6B).");
            RuleFor(x => x.BackgroundColor).Must(BeColorOrEmpty).WithMessage("Màu nền phải là mã HEX 6 số (vd #FFFFFF).");
            RuleFor(x => x.NavBarColor).Must(BeColorOrEmpty).WithMessage("Màu thanh điều hướng phải là mã HEX 6 số.");
            RuleFor(x => x.FontFamily).Must(font => font is null || FontName.IsMatch(font))
                .WithMessage("Tên font chỉ gồm chữ, số và dấu cách (tối đa 40 ký tự).");
            RuleFor(x => x.TextScale).InclusiveBetween(0.8, 1.5).WithMessage("Hệ số cỡ chữ phải từ 0,8 đến 1,5.");
            RuleFor(x => x.LogoUrl).Must(url => url is null || (url.Length <= 500 && IsHttps(url)))
                .WithMessage("Đường dẫn logo phải là địa chỉ https (tối đa 500 ký tự).");
        }

        private static bool BeColorOrEmpty(string? color) => color is null || HexColor.IsMatch(color);
    }
}
