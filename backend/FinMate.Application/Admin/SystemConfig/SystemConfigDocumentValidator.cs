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
        RuleFor(x => x.Forecast).NotNull().SetValidator(new ForecastValidator());
        RuleFor(x => x.Groups).NotNull().SetValidator(new GroupValidator());
        RuleFor(x => x.Support).NotNull().SetValidator(new SupportValidator());
        RuleFor(x => x.Monitoring).NotNull().SetValidator(new MonitoringValidator());
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

    private sealed class GroupValidator : AbstractValidator<GroupSettings>
    {
        public GroupValidator()
        {
            RuleFor(x => x.MaxMembers).InclusiveBetween(2, 100).WithMessage("Số thành viên tối đa của nhóm phải từ 2 đến 100.");
            RuleFor(x => x.MaxGroupsPerUser).InclusiveBetween(1, 50).WithMessage("Số nhóm tối đa của mỗi người phải từ 1 đến 50.");
            RuleFor(x => x.MaxPendingInvitations).InclusiveBetween(1, 200).WithMessage("Số lời mời đang chờ tối đa phải từ 1 đến 200.");
            RuleFor(x => x.InvitationTtlDays).InclusiveBetween(1, 30).WithMessage("Thời hạn lời mời phải từ 1 đến 30 ngày.");
        }
    }

    private sealed class MonitoringValidator : AbstractValidator<MonitoringSettings>
    {
        public MonitoringValidator()
        {
            RuleFor(x => x.ProbeFailureThreshold).InclusiveBetween(1, 60).WithMessage("Số lần kiểm tra lỗi liên tiếp phải từ 1 đến 60.");
            RuleFor(x => x.ProbeRecoveryThreshold).InclusiveBetween(1, 60).WithMessage("Số lần kiểm tra tốt liên tiếp phải từ 1 đến 60.");
            RuleFor(x => x.ErrorCountThreshold).InclusiveBetween(1, 10000).WithMessage("Ngưỡng số lỗi phải từ 1 đến 10000.");
            RuleFor(x => x.ErrorWindowMinutes).InclusiveBetween(1, 1440).WithMessage("Cửa sổ đếm lỗi phải từ 1 đến 1440 phút.");
            RuleFor(x => x.ErrorQuietMinutes).InclusiveBetween(1, 1440).WithMessage("Thời gian tự đóng phải từ 1 đến 1440 phút.");
            RuleFor(x => x.ProviderFailureThreshold).InclusiveBetween(1, 1000).WithMessage("Ngưỡng lỗi nhà cung cấp phải từ 1 đến 1000.");
            RuleFor(x => x.ReminderMinutes).InclusiveBetween(1, 1440).WithMessage("Thời gian nhắc lại phải từ 1 đến 1440 phút.");
            RuleFor(x => x.ErrorRetentionDays).InclusiveBetween(1, 365).WithMessage("Thời gian giữ lỗi phải từ 1 đến 365 ngày.");
        }
    }

    private sealed class SupportValidator : AbstractValidator<SupportSettings>
    {
        private const int MaxMinutes = 60 * 24 * 30;

        public SupportValidator()
        {
            RuleFor(x => x.P1FirstResponseMinutes).InclusiveBetween(1, MaxMinutes).LessThanOrEqualTo(x => x.P1ResolveMinutes).WithMessage("Hạn của P1 không hợp lệ.");
            RuleFor(x => x.P2FirstResponseMinutes).InclusiveBetween(1, MaxMinutes).LessThanOrEqualTo(x => x.P2ResolveMinutes).WithMessage("Hạn của P2 không hợp lệ.");
            RuleFor(x => x.P3FirstResponseMinutes).InclusiveBetween(1, MaxMinutes).LessThanOrEqualTo(x => x.P3ResolveMinutes).WithMessage("Hạn của P3 không hợp lệ.");
            RuleFor(x => x.P1ResolveMinutes).InclusiveBetween(1, MaxMinutes).WithMessage("Hạn của P1 không hợp lệ.");
            RuleFor(x => x.P2ResolveMinutes).InclusiveBetween(1, MaxMinutes).WithMessage("Hạn của P2 không hợp lệ.");
            RuleFor(x => x.P3ResolveMinutes).InclusiveBetween(1, MaxMinutes).WithMessage("Hạn của P3 không hợp lệ.");
            RuleFor(x => x.MaxOpenTicketsPerUser).InclusiveBetween(1, 100).WithMessage("Số yêu cầu đang mở tối đa phải từ 1 đến 100.");
            RuleFor(x => x.MaxAttachmentsPerMessage).InclusiveBetween(1, 10).WithMessage("Số ảnh mỗi tin nhắn phải từ 1 đến 10.");
            RuleFor(x => x.AutoCloseResolvedDays).InclusiveBetween(1, 90).WithMessage("Số ngày tự đóng phải từ 1 đến 90.");
            RuleFor(x => x.AttachmentRetentionDays).InclusiveBetween(1, 90).WithMessage("Số ngày giữ ảnh phải từ 1 đến 90.");
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

    private sealed class ForecastValidator : AbstractValidator<ForecastSettings>
    {
        private const int MaxHolidays = 30;

        public ForecastValidator()
        {
            RuleFor(x => x.HistoryWindowDays).InclusiveBetween(30, 365).WithMessage("Cửa sổ lịch sử phải từ 30 đến 365 ngày.");
            RuleFor(x => x).Must(x => x.MinHistoryDays >= 1 && x.MinHistoryDays <= x.HistoryWindowDays)
                .WithName(nameof(ForecastSettings.MinHistoryDays)).WithMessage("Số ngày dữ liệu tối thiểu phải từ 1 đến độ dài cửa sổ lịch sử.");
            RuleFor(x => x.ReservePercentOfIncome).InclusiveBetween(0, 50).WithMessage("Dự phòng mặc định phải từ 0 đến 50% thu nhập.");
            RuleFor(x => x.MaxHorizonDays).InclusiveBetween(7, 365).WithMessage("Kỳ dự báo tối đa phải từ 7 đến 365 ngày.");
            RuleFor(x => x.AmountTolerancePercent).InclusiveBetween(0, 50).WithMessage("Độ lệch số tiền của khoản định kỳ phải từ 0 đến 50%.");
            RuleFor(x => x).Must(x => x.MonthlyMinIntervalDays >= 20 && x.MonthlyMinIntervalDays <= x.MonthlyMaxIntervalDays && x.MonthlyMaxIntervalDays <= 40)
                .WithName(nameof(ForecastSettings.MonthlyMinIntervalDays)).WithMessage("Khoảng cách chu kỳ tháng phải nằm trong 20–40 ngày và tối thiểu không lớn hơn tối đa.");
            RuleFor(x => x).Must(x => x.WeeklyMinIntervalDays >= 5 && x.WeeklyMinIntervalDays <= x.WeeklyMaxIntervalDays && x.WeeklyMaxIntervalDays <= 9)
                .WithName(nameof(ForecastSettings.WeeklyMinIntervalDays)).WithMessage("Khoảng cách chu kỳ tuần phải nằm trong 5–9 ngày và tối thiểu không lớn hơn tối đa.");
            RuleFor(x => x).Must(x => x.SuggestMinOccurrences >= 2 && x.SuggestMinOccurrences <= x.AutoActivateOccurrences && x.AutoActivateOccurrences <= 12)
                .WithName(nameof(ForecastSettings.SuggestMinOccurrences)).WithMessage("Số lần lặp để đề xuất phải ≥ 2 và không lớn hơn số lần để tự ghi nhận (tối đa 12).");
            RuleFor(x => x.OverdueGraceDays).InclusiveBetween(0, 31).WithMessage("Số ngày ân hạn của khoản định kỳ phải từ 0 đến 31.");
            RuleFor(x => x.MaxScenarioItems).InclusiveBetween(1, 50).WithMessage("Số khoản giả định tối đa phải từ 1 đến 50.");
            RuleFor(x => x.LowBalanceHorizonDays).InclusiveBetween(3, 60).WithMessage("Tầm nhìn cảnh báo số dư thấp phải từ 3 đến 60 ngày.");
            RuleFor(x => x.LowBalanceCooldownDays).InclusiveBetween(1, 30).WithMessage("Khoảng cách giữa hai cảnh báo số dư thấp phải từ 1 đến 30 ngày.");
            RuleFor(x => x.InaccurateSuppressDays).InclusiveBetween(1, 60).WithMessage("Thời gian tắt cảnh báo sau phản hồi 'không chính xác' phải từ 1 đến 60 ngày.");
            RuleFor(x => x.BudgetRiskMinElapsedDays).InclusiveBetween(1, 28).WithMessage("Số ngày tối thiểu để dùng nhịp chi tháng này phải từ 1 đến 28.");
            RuleFor(x => x.BudgetRiskMinDaysRemaining).InclusiveBetween(1, 28).WithMessage("Số ngày còn lại tối thiểu để báo vượt ngân sách sớm phải từ 1 đến 28.");
            RuleFor(x => x.BillReminderDaysBefore).InclusiveBetween(0, 14).WithMessage("Nhắc hóa đơn trước hạn phải từ 0 đến 14 ngày.");
            RuleFor(x => x.IncomeLateDays).InclusiveBetween(1, 15).WithMessage("Số ngày trễ trước khi báo thu nhập trễ phải từ 1 đến 15.");
            RuleFor(x => x.IrregularIncomeMonths).InclusiveBetween(2, 12).WithMessage("Số tháng ước tính thu nhập không đều phải từ 2 đến 12.");
            RuleFor(x => x.QuietHoursStartMinute).InclusiveBetween(0, 1439).WithMessage("Giờ bắt đầu yên lặng không hợp lệ.");
            RuleFor(x => x.QuietHoursEndMinute).InclusiveBetween(0, 1439).WithMessage("Giờ kết thúc yên lặng không hợp lệ.");
            RuleFor(x => x.AnomalyLookbackDays).InclusiveBetween(30, 365).WithMessage("Cửa sổ so sánh bất thường phải từ 30 đến 365 ngày.");
            RuleFor(x => x.AnomalyMinCategorySamples).InclusiveBetween(3, 50).WithMessage("Số khoản tối thiểu để so theo danh mục phải từ 3 đến 50.");
            RuleFor(x => x.AnomalyMinMerchantSamples).InclusiveBetween(2, 50).WithMessage("Số lần tối thiểu để so theo người nhận phải từ 2 đến 50.");
            RuleFor(x => x).Must(x => x.AnomalyRatioPercent >= 150 && x.AnomalyRatioPercent <= x.AnomalyHighRatioPercent && x.AnomalyHighRatioPercent <= 5000)
                .WithName(nameof(ForecastSettings.AnomalyRatioPercent)).WithMessage("Tỉ lệ bất thường phải ≥ 150% và không lớn hơn tỉ lệ mức cao (tối đa 5000%).");
            RuleFor(x => x.AnomalyMinAmountCents).InclusiveBetween(0, 100_000_000).WithMessage("Số tiền tối thiểu để xét bất thường phải từ 0 đến 100 triệu.");
            RuleFor(x => x.DuplicateWindowMinutes).InclusiveBetween(1, 120).WithMessage("Cửa sổ nghi trùng phải từ 1 đến 120 phút.");
            RuleFor(x => x.NormalFeedbackTolerancePercent).InclusiveBetween(100, 300).WithMessage("Dung sai sau phản hồi 'bình thường' phải từ 100% đến 300%.");
            RuleFor(x => x.AnomalyScanDays).InclusiveBetween(1, 14).WithMessage("Số ngày quét bất thường phải từ 1 đến 14.");
            RuleFor(x => x).Must(x => x.CategoryForecastMinMonths >= 1 && x.CategoryForecastMinMonths <= x.CategoryForecastMonths && x.CategoryForecastMonths <= 12)
                .WithName(nameof(ForecastSettings.CategoryForecastMonths)).WithMessage("Số tháng dự báo theo danh mục phải từ 1 đến 12 và không nhỏ hơn số tháng tối thiểu.");
            RuleFor(x => x.CategoryChangePercent).InclusiveBetween(1, 100).WithMessage("Ngưỡng 'thay đổi nhiều' phải từ 1 đến 100%.");
            RuleFor(x => x.BudgetSuggestionRoundingCents).InclusiveBetween(1, 1_000_000).WithMessage("Bội số làm tròn hạn mức đề xuất phải từ 1 đến 1 triệu.");
            RuleFor(x => x.GoalRateMonths).InclusiveBetween(1, 24).WithMessage("Số tháng tính tốc độ góp mục tiêu phải từ 1 đến 24.");
            RuleFor(x => x.Holidays).NotNull().Must(h => h.Count <= MaxHolidays).WithMessage($"Tối đa {MaxHolidays} khoảng ngày lễ.");
            RuleForEach(x => x.Holidays).ChildRules(h =>
            {
                h.RuleFor(x => x.Name).NotEmpty().MaximumLength(60).WithMessage("Tên ngày lễ không được trống và tối đa 60 ký tự.");
                h.RuleFor(x => x).Must(x => x.Start <= x.End && x.End.DayNumber - x.Start.DayNumber <= 31)
                    .WithName(nameof(ForecastHoliday.Start)).WithMessage("Ngày lễ phải có ngày bắt đầu không sau ngày kết thúc và dài tối đa 32 ngày.");
                h.RuleFor(x => x.MultiplierPercent).InclusiveBetween(50, 300).WithMessage("Hệ số chi tiêu ngày lễ phải từ 50 đến 300%.");
            });
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
