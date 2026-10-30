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
            RuleFor(x => x.BudgetNearPercent).InclusiveBetween(1, 100).WithMessage("Ngưỡng sắp chạm phải từ 1 đến 100%.");
            RuleFor(x => x.BudgetOverPercent).InclusiveBetween(50, 200).WithMessage("Ngưỡng vượt phải từ 50 đến 200%.");
            RuleFor(x => x).Must(x => x.BudgetNearPercent < x.BudgetOverPercent)
                .WithName(nameof(LimitSettings.BudgetNearPercent)).WithMessage("Ngưỡng sắp chạm phải nhỏ hơn ngưỡng vượt.");
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
