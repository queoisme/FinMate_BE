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
        RuleFor(x => x.NotificationPipeline).NotNull().SetValidator(new NotificationPipelineValidator());
        RuleFor(x => x.FinanceMethods).NotNull().SetValidator(new FinanceMethodValidator());
        RuleFor(x => x.Forecast).NotNull().SetValidator(new ForecastValidator());
        RuleFor(x => x.Groups).NotNull().SetValidator(new GroupValidator());
        RuleFor(x => x.Support).NotNull().SetValidator(new SupportValidator());
        RuleFor(x => x.Monitoring).NotNull().SetValidator(new MonitoringValidator());
        RuleFor(x => x.Agent).NotNull();
        RuleFor(x => x.Agent.CutSuggestionPercent).InclusiveBetween(0, 50).When(x => x.Agent is not null)
            .WithMessage("Mức giảm chi giả định của gợi ý cắt giảm phải từ 0 đến 50 phần trăm.");
        RuleFor(x => x.Agent.SurveySavingShare).NotNull()
            .Must(m => m is null || m.All(p => System.Text.RegularExpressions.Regex.IsMatch(p.Key, "^[a-z0-9_]{1,50}:[A-Za-z0-9]{1,10}$") && p.Value is >= 0 and <= 100))
            .When(x => x.Agent is not null)
            .WithMessage("Mức tiết kiệm theo khảo sát phải có khóa dạng 'mã_câu_hỏi:mã_đáp_án' và giá trị từ 0 đến 100.");
        RuleFor(x => x.Agent.SurveySpendEstimate).NotNull()
            .Must(m => m is null || m.All(p => System.Text.RegularExpressions.Regex.IsMatch(p.Key, "^[a-z0-9_]{1,50}:[A-Za-z0-9]{1,10}$") && p.Value is >= 0 and <= 100_000_000_000))
            .When(x => x.Agent is not null)
            .WithMessage("Mức chi theo khảo sát phải có khóa dạng 'mã_câu_hỏi:mã_đáp_án' và giá trị từ 0 đến 100 tỷ đồng.");
        RuleFor(x => x.Agent.Texts).NotNull().Custom((texts, ctx) =>
        {
            foreach (var (key, template) in texts ?? new Dictionary<string, string>())
            {
                if (FinMate.Application.Agent.AgentTexts.Problem(key, template) is { } problem)
                {
                    ctx.AddFailure(problem);
                }
            }
        }).When(x => x.Agent is not null);
        RuleFor(x => x.EconomicFeeds).NotNull().SetValidator(new EconomicFeedsValidator());
        RuleFor(x => x.MarketData).NotNull().SetValidator(new MarketDataValidator());
        RuleFor(x => x.EconomicFeeds.CpiIndexUrl)
            .Must((doc, url) => string.IsNullOrEmpty(url) || !IsHttps(url) || FinMate.Application.MarketData.MarketTrust.TierOf(url, doc.MarketData) == MarketTrustedDomain.Official)
            .WithMessage("Địa chỉ nguồn CPI phải thuộc một tên miền hạng official trong danh sách nguồn uy tín.")
            .When(x => x.EconomicFeeds is not null && x.MarketData?.TrustedDomains is { } list && list.All(d => d is not null));
        RuleFor(x => x.Community).NotNull().SetValidator(new CommunitySettingsValidator());
        RuleFor(x => x.Categories).NotNull().SetValidator(new CategorySettingsValidator());
        RuleFor(x => x.Assistant).NotNull().SetValidator(new AssistantValidator());
        RuleFor(x => x.Refunds).NotNull().SetValidator(new RefundValidator());
        RuleFor(x => x.Duplicates).NotNull().SetValidator(new DuplicateValidator());
        RuleFor(x => x.Transfers).NotNull().SetValidator(new TransferValidator());
        RuleFor(x => x.Snapshots).NotNull().SetValidator(new SnapshotValidator());
        RuleFor(x => x.ListenerHealth).NotNull().SetValidator(new ListenerHealthValidator());
    }

    private sealed class AssistantValidator : AbstractValidator<AssistantSettings>
    {
        public AssistantValidator()
        {
            RuleFor(x => x.MaxSuggestions).InclusiveBetween(0, 10).WithMessage("Số câu gợi ý tối đa phải từ 0 đến 10.");
            RuleFor(x => x.Suggestions).NotNull().WithMessage("Danh sách câu gợi ý không được thiếu.");
            RuleForEach(x => x.Suggestions).ChildRules(s =>
            {
                s.RuleFor(t => t.Key).NotEmpty().MaximumLength(40).WithMessage("Mã câu gợi ý phải có và tối đa 40 ký tự.");
                s.RuleFor(t => t.Text).NotEmpty().MaximumLength(120).WithMessage("Câu gợi ý phải có và tối đa 120 ký tự.");
                s.RuleFor(t => t.When).Must(w => AssistantSettings.SuggestionContexts.Contains(w)).WithMessage("Ngữ cảnh câu gợi ý không hợp lệ.");
            });
            RuleFor(x => x.Suggestions).Must(l => l.Select(t => t.Key).Distinct().Count() == l.Count).WithMessage("Mã câu gợi ý bị trùng.");
            RuleFor(x => x.CategorySourceConfidence).NotNull().Must(d => d.Values.All(v => v is >= 0 and <= 1)).WithMessage("Độ tin cậy theo nguồn phải nằm trong khoảng 0 đến 1.");
        }
    }

    private sealed class CategorySettingsValidator : AbstractValidator<CategorySettings>
    {
        private static readonly Regex ColorKey = new("^[a-z0-9_-]{1,24}$", RegexOptions.Compiled);

        public CategorySettingsValidator()
        {
            RuleFor(x => x.Palette).NotNull().Must(p => p is { Count: >= 1 and <= 24 })
                .WithMessage("Bảng màu danh mục phải có từ 1 đến 24 màu.");
            RuleFor(x => x.Palette).Must(p => p is null || p.Select(c => c.Key).Distinct().Count() == p.Count)
                .WithMessage("Mã (key) của các màu trong bảng màu không được trùng nhau.");
            RuleForEach(x => x.Palette).ChildRules(color =>
            {
                color.RuleFor(c => c.Key).Must(k => k is not null && ColorKey.IsMatch(k)).WithMessage("Mã màu chỉ gồm chữ thường, số, gạch ngang hoặc gạch dưới (tối đa 24 ký tự).");
                color.RuleFor(c => c.Hex).Must(h => h is not null && HexColor.IsMatch(h)).WithMessage("Giá trị màu phải có dạng #RRGGBB.");
                color.RuleFor(c => c.Name).NotEmpty().MaximumLength(40).WithMessage("Tên màu không được để trống và tối đa 40 ký tự.");
            });
        }
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
            RuleFor(x => x.MaxFundsPerGroup).InclusiveBetween(1, 20).WithMessage("Số quỹ tối đa của mỗi nhóm phải từ 1 đến 20.");
            RuleFor(x => x.DebtReminderCooldownHours).InclusiveBetween(1, 168).WithMessage("Khoảng cách giữa hai lần nhắc nợ phải từ 1 đến 168 giờ.");
            RuleFor(x => x.InviteLinkBaseUrl).Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps).WithMessage("Link mời phải là địa chỉ https.");
            RuleFor(x => x.UserSearchLimit).InclusiveBetween(1, 1000).WithMessage("Số lần tìm người tối đa phải từ 1 đến 1000.");
            RuleFor(x => x.UserSearchWindowMinutes).InclusiveBetween(1, 1440).WithMessage("Cửa sổ giới hạn tìm người phải từ 1 đến 1440 phút.");
            RuleFor(x => x.MaxInviteSuggestions).InclusiveBetween(1, 50).WithMessage("Số gợi ý mời phải từ 1 đến 50.");
            RuleFor(x => x.MaxBillsPerPage).InclusiveBetween(10, 200).WithMessage("Số hóa đơn mỗi trang phải từ 10 đến 200.");
        }
    }

    private sealed class EconomicFeedsValidator : AbstractValidator<EconomicFeedsSettings>
    {
        public EconomicFeedsValidator()
        {
            RuleFor(x => x.CpiIndexUrl).NotNull().Must(url => url == string.Empty || IsHttps(url)).WithMessage("Địa chỉ nguồn CPI phải là https hợp lệ hoặc để trống.");
            RuleFor(x => x.ScheduleDayOfMonth).InclusiveBetween(1, 28).WithMessage("Ngày chạy phải từ 1 đến 28.");
            RuleFor(x => x.ScheduleHour).InclusiveBetween(0, 23).WithMessage("Giờ chạy phải từ 0 đến 23.");
            RuleFor(x => x.AutoApproveMaxDeltaBps).InclusiveBetween(0, 10000).WithMessage("Ngưỡng tự duyệt phải từ 0 đến 10000 điểm cơ bản.");
            RuleFor(x => x.MinRateBps).InclusiveBetween(-10000, 100000).WithMessage("Giới hạn dưới của tốc độ tăng phải từ -10000 đến 100000 điểm cơ bản.");
            RuleFor(x => x.MaxRateBps).GreaterThan(x => x.MinRateBps).WithMessage("Giới hạn trên của tốc độ tăng phải lớn hơn giới hạn dưới.");
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
            RuleFor(x => x.ErrorHints).NotNull().Must(h => h is { Count: <= 100 }).WithMessage("Tối đa 100 chú giải lỗi.");
            RuleForEach(x => x.ErrorHints).ChildRules(hint =>
            {
                hint.RuleFor(h => h.Match).NotEmpty().MaximumLength(200).WithMessage("Chuỗi khớp tên lỗi là bắt buộc (tối đa 200 ký tự).");
                hint.RuleFor(h => h.Title).NotEmpty().MaximumLength(100).WithMessage("Tên lỗi là bắt buộc (tối đa 100 ký tự).");
                hint.RuleFor(h => h.Explanation).MaximumLength(500).WithMessage("Giải thích tối đa 500 ký tự.");
                hint.RuleFor(h => h.Action).MaximumLength(500).WithMessage("Hướng xử lý tối đa 500 ký tự.");
            });
        }
    }

    private sealed class MarketDataValidator : AbstractValidator<MarketDataSettings>
    {
        private static bool ValidKeywords(IReadOnlyList<string> keywords)
            => keywords is { Count: <= 50 } && keywords.All(word => !string.IsNullOrWhiteSpace(word) && word.Length <= 50);

        public MarketDataValidator()
        {
            RuleFor(x => x.MinAgreeingSources).InclusiveBetween(1, 10).WithMessage("Số nguồn phải khớp phải từ 1 đến 10.");
            RuleFor(x => x.MaxDocumentsPerRun).InclusiveBetween(1, 200).WithMessage("Số bài mỗi lượt phải từ 1 đến 200.");
            RuleFor(x => x.MinPriceCents).GreaterThan(0).LessThan(x => x.MaxPriceCents).WithMessage("Giá tối thiểu phải lớn hơn 0 và nhỏ hơn giá tối đa.");
            RuleFor(x => x.MaxChangePercent).InclusiveBetween(1, 100).WithMessage("Mức đổi giá tối đa phải từ 1 đến 100%.");
            RuleFor(x => x.ReferenceFuelKey).Must(key => FinMate.Application.MarketData.FuelKeys.All.Contains(key)).WithMessage("Loại xăng so sánh không hợp lệ.");
            RuleFor(x => x.RiseThresholdPercent).InclusiveBetween(1, 100).WithMessage("Ngưỡng tăng giá phải từ 1 đến 100%.");
            RuleFor(x => x.CompareWindowDays).InclusiveBetween(1, 90).WithMessage("Số ngày so sánh phải từ 1 đến 90.");
            RuleFor(x => x.AlertCooldownDays).InclusiveBetween(0, 365).WithMessage("Thời gian giãn cảnh báo phải từ 0 đến 365 ngày.");
            RuleFor(x => x.SpendLookbackDays).InclusiveBetween(7, 365).WithMessage("Số ngày xét chi tiêu phải từ 7 đến 365.");
            RuleFor(x => x.MinCategoryTransactions).InclusiveBetween(1, 100).WithMessage("Số giao dịch tối thiểu phải từ 1 đến 100.");
            RuleFor(x => x.LastSpendMaxDays).InclusiveBetween(1, 365).WithMessage("Số ngày chi gần nhất phải từ 1 đến 365.");
            RuleFor(x => x.MinSpendSharePercent).InclusiveBetween(0, 100).WithMessage("Tỉ trọng chi tối thiểu phải từ 0 đến 100%.");
            RuleFor(x => x.MinExtraCostCents).GreaterThanOrEqualTo(0).WithMessage("Mức tốn thêm tối thiểu không được âm.");
            RuleFor(x => x.CostTypes).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Phải có danh sách loại chi phí.")
                .Must(types => types.Count is >= 1 and <= 20).WithMessage("Phải có từ 1 đến 20 loại chi phí.")
                .Must(types => types.All(t => t is not null)).WithMessage("Loại chi phí không được để trống.")
                .Must(types => types.Any(t => t.Enabled)).WithMessage("Phải bật ít nhất một loại chi phí. Muốn tắt cảnh báo thì dùng features.marketAlerts.")
                .Must(types => types.Select(t => t.Key).Distinct().Count() == types.Count).WithMessage("Mã loại chi phí không được trùng.")
                .Must(types => types.All(t => !t.HasPriceTable || t.Key == "fuel")).WithMessage("Hiện chỉ loại fuel có bảng giá, vì bộ đọc bảng chỉ đọc giá xăng.");
            RuleForEach(x => x.CostTypes).Cascade(CascadeMode.Stop).NotNull().ChildRules(type =>
            {
                type.RuleFor(t => t.Key).Cascade(CascadeMode.Stop).NotEmpty().Matches(@"\A[a-z][a-z_]{1,28}[a-z]\z").NotEqual("other").WithMessage("Mã loại chi phí chỉ gồm chữ thường và dấu gạch dưới, tối đa 30 ký tự, không được là other.");
                type.RuleFor(t => t.Label).NotEmpty().MaximumLength(50).WithMessage("Tên loại chi phí từ 1 đến 50 ký tự.");
                type.RuleFor(t => t.AiDescription).NotEmpty().MaximumLength(200).WithMessage("Mô tả cho AI từ 1 đến 200 ký tự.");
                type.RuleFor(t => t.TransactionKeywords).Must(ValidKeywords).WithMessage("Tối đa 50 từ khóa giao dịch, mỗi từ khóa tối đa 50 ký tự.");
                type.RuleFor(t => t.TransactionExcludes).Must(ValidKeywords).WithMessage("Tối đa 50 cụm loại trừ, mỗi cụm tối đa 50 ký tự.");
                type.RuleFor(t => t.NewsKeywords).Must(ValidKeywords).WithMessage("Tối đa 50 từ khóa tin bài, mỗi từ khóa tối đa 50 ký tự.");
                type.RuleFor(t => t.CategorySlugs).Must(slugs => slugs is { Count: <= 10 }).WithMessage("Tối đa 10 danh mục cho một loại chi phí.");
                type.RuleFor(t => t).Must(t => t.TransactionKeywords is { Count: > 0 } || t.CategorySlugs is { Count: > 0 }).WithMessage("Loại chi phí phải có từ khóa giao dịch hoặc danh mục.");
            });
            RuleFor(x => x.TrustedDomains).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Phải có danh sách nguồn uy tín.")
                .Must(list => list.Count <= 200).WithMessage("Tối đa 200 tên miền uy tín.")
                .Must(list => list.All(d => d is not null)).WithMessage("Tên miền uy tín không được để trống.")
                .Must(list => list.Select(d => d.Domain).Distinct().Count() == list.Count).WithMessage("Tên miền uy tín không được trùng.");
            RuleForEach(x => x.TrustedDomains).Cascade(CascadeMode.Stop).NotNull().ChildRules(domain =>
            {
                domain.RuleFor(d => d.Domain).Cascade(CascadeMode.Stop).NotEmpty().MaximumLength(100)
                    .Matches(@"\A[a-z0-9-]+(\.[a-z0-9-]+)+\z")
                    .Must(d => FinMate.Application.MarketData.MarketDomains.OfHost(d) == d)
                    .WithMessage("Tên miền uy tín phải là tên miền gốc viết thường, ví dụ 24h.com.vn (tên miền con tính chung).");
                domain.RuleFor(d => d.Tier).Must(t => t is MarketTrustedDomain.Official or MarketTrustedDomain.Licensed).WithMessage("Hạng nguồn phải là official hoặc licensed.");
                domain.RuleFor(d => d.Owner).NotEmpty().MaximumLength(200).WithMessage("Đơn vị chủ quản từ 1 đến 200 ký tự.");
                domain.RuleFor(d => d.LicenseRef).NotNull().MaximumLength(300).WithMessage("Số giấy phép tối đa 300 ký tự.");
                domain.RuleFor(d => d.LicenseRef).NotEmpty().When(d => d.Tier == MarketTrustedDomain.Licensed).WithMessage("Nguồn có giấy phép phải ghi số giấy phép.");
                domain.RuleFor(d => d.CheckedAt).Must(d => string.IsNullOrEmpty(d) || DateOnly.TryParseExact(d, "yyyy-MM-dd", out _)).WithMessage("Ngày kiểm phải có dạng yyyy-MM-dd.");
            });
            RuleFor(x => x.Gold).NotNull().ChildRules(gold =>
            {
                gold.RuleFor(g => g.MinPriceCents).GreaterThan(0).LessThan(g => g.MaxPriceCents).WithMessage("Giá vàng tối thiểu phải lớn hơn 0 và nhỏ hơn giá tối đa.");
                gold.RuleFor(g => g.MaxChangePercent).InclusiveBetween(1, 100).WithMessage("Mức đổi giá vàng tối đa phải từ 1 đến 100%.");
                gold.RuleFor(g => g.MaxSpreadPercent).InclusiveBetween(0, 100).WithMessage("Chênh mua bán tối đa phải từ 0 đến 100%.");
                gold.RuleFor(g => g.Rows).Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("Phải có danh sách loại vàng.")
                    .Must(rows => rows.Count <= 10 && rows.All(r => r is not null)).WithMessage("Tối đa 10 loại vàng, không được để trống.")
                    .Must(rows => rows.Select(r => r.Key).Distinct().Count() == rows.Count).WithMessage("Mã loại vàng không được trùng.");
                gold.RuleForEach(g => g.Rows).ChildRules(row =>
                {
                    row.RuleFor(r => r.Key).Matches(@"\A[a-z][a-z0-9_]{1,28}[a-z0-9]\z").WithMessage("Mã loại vàng chỉ gồm chữ thường, số và gạch dưới, tối đa 30 ký tự.");
                    row.RuleFor(r => r.Label).NotEmpty().MaximumLength(50).WithMessage("Tên loại vàng từ 1 đến 50 ký tự.");
                    row.RuleFor(r => r.Names).Must(names => names is { Count: >= 1 and <= 10 } && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 50)).WithMessage("Mỗi loại vàng có 1 đến 10 tên, mỗi tên tối đa 50 ký tự.");
                });
                gold.RuleFor(g => g.RowWords).Must(ValidKeywords).WithMessage("Tối đa 50 chữ được phép trong dòng giá vàng, mỗi chữ tối đa 50 ký tự.");
                gold.RuleFor(g => g.Units).Must(units => units is { Count: >= 1 and <= 20 } && units.All(u => u is not null && !string.IsNullOrWhiteSpace(u.Text) && u.Text.Length <= 50 && u.Multiplier is >= 1 and <= 1_000_000_000))
                    .WithMessage("Có 1 đến 20 đơn vị giá vàng, mỗi đơn vị có chữ (tối đa 50 ký tự) và hệ số từ 1 đến 1 tỉ.");
            });
            RuleFor(x => x.GoalTopics).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Phải có danh sách chủ đề mục tiêu.")
                .Must(topics => topics.Count <= 20 && topics.All(t => t is not null)).WithMessage("Tối đa 20 chủ đề mục tiêu, không được để trống.")
                .Must(topics => topics.Select(t => t.Key).Distinct().Count() == topics.Count).WithMessage("Mã chủ đề mục tiêu không được trùng.")
                .Must((settings, topics) => topics.All(t => settings.Gold?.Rows?.Any(r => r?.Key == t.GoldKey) == true)).WithMessage("Mỗi chủ đề mục tiêu phải trỏ tới một loại vàng có trong marketData.gold.rows.");
            RuleForEach(x => x.GoalTopics).ChildRules(topic =>
            {
                topic.RuleFor(t => t.Key).Matches(@"\A[a-z][a-z0-9_]{1,30}\z").WithMessage("Mã chủ đề mục tiêu chỉ gồm chữ thường, số và gạch dưới, 2 đến 31 ký tự.");
                topic.RuleFor(t => t.Label).NotEmpty().MaximumLength(50).WithMessage("Tên chủ đề mục tiêu từ 1 đến 50 ký tự.");
                topic.RuleFor(t => t.NameKeywords).Must(ValidKeywords).WithMessage("Tối đa 50 từ gợi ý, mỗi từ tối đa 50 ký tự.");
            });
            RuleFor(x => x.GoalAlerts).NotNull().ChildRules(alerts =>
            {
                alerts.RuleFor(a => a.MinChangePercent).InclusiveBetween(1, 100).WithMessage("Mức tăng giá tối thiểu phải từ 1 đến 100%.");
                alerts.RuleFor(a => a.MinShiftDays).InclusiveBetween(1, 3650).WithMessage("Số ngày lùi tối thiểu phải từ 1 đến 3650.");
                alerts.RuleFor(a => a.CooldownDays).InclusiveBetween(0, 365).WithMessage("Thời gian giãn cảnh báo mục tiêu phải từ 0 đến 365 ngày.");
            });
            RuleFor(x => x.AssumptionRules).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Phải có danh sách luật soạn giả định.")
                .Must(rules => rules.Count <= 20 && rules.All(r => r is not null)).WithMessage("Tối đa 20 luật soạn giả định, không được để trống.")
                .Must(rules => rules.Select(r => r.AssumptionKey).Distinct().Count() == rules.Count).WithMessage("Mỗi khóa giả định chỉ có một luật.")
                .Must((settings, rules) => rules.All(r => settings.Gold?.Rows?.Any(g => g?.Key == r.GoldKey) == true)).WithMessage("Mỗi luật soạn giả định phải trỏ tới một loại vàng có trong marketData.gold.rows.");
            RuleForEach(x => x.AssumptionRules).ChildRules(rule =>
            {
                rule.RuleFor(r => r.AssumptionKey).Matches("^[a-z][a-z0-9_]{2,47}$").WithMessage("Khóa giả định gồm chữ thường, số, gạch dưới; bắt đầu bằng chữ; 3–48 ký tự.");
                rule.RuleFor(r => r.WindowDays).InclusiveBetween(30, 3650).WithMessage("Cửa sổ tính tốc độ phải từ 30 đến 3650 ngày.");
                rule.RuleFor(r => r.MinDeltaBps).InclusiveBetween(0, 10000).WithMessage("Mức lệch để soạn nháp mới phải từ 0 đến 10000 điểm cơ bản.");
            });
            RuleFor(x => x.FuelReleaseLinkText).NotEmpty().MaximumLength(200).WithMessage("Chữ của link thông cáo giá xăng từ 1 đến 200 ký tự.");
            RuleFor(x => x.FuelReleaseNames).Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("Phải có danh sách tên hàng trong thông cáo giá xăng.")
                .Must(list => list.Count <= 20 && list.All(n => n is not null)).WithMessage("Tối đa 20 tên hàng, không được để trống.")
                .Must(list => list.Select(n => n.FuelKey).Distinct().Count() == list.Count).WithMessage("Mỗi loại xăng dầu chỉ được một tên hàng.");
            RuleForEach(x => x.FuelReleaseNames).ChildRules(name =>
            {
                name.RuleFor(n => n.Name).NotEmpty().MaximumLength(100).WithMessage("Tên hàng từ 1 đến 100 ký tự.");
                name.RuleFor(n => n.FuelKey).Must(key => FinMate.Application.MarketData.FuelKeys.All.Contains(key)).WithMessage("Loại xăng dầu của tên hàng không hợp lệ.");
            });
            RuleFor(x => x.MinEventDomains).InclusiveBetween(1, 10).WithMessage("Số tên miền phải từ 1 đến 10.");
            RuleFor(x => x.MaxAiDocumentsPerDay).InclusiveBetween(0, 5000).WithMessage("Số bài gửi AI mỗi ngày phải từ 0 đến 5000.");
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
            RuleFor(x => x).Must(x => x.MinCoveragePercent >= 0 && x.MinCoveragePercent <= x.GoodCoveragePercent && x.GoodCoveragePercent <= 100)
                .WithName(nameof(ForecastSettings.GoodCoveragePercent)).WithMessage("Ngưỡng độ phủ phải nằm trong 0–100% và mức tối thiểu không lớn hơn mức tốt.");
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
            RuleFor(x => x.PurchaseDefaultHorizonDays).InclusiveBetween(1, 90).WithMessage("Mốc mặc định khi đánh giá khoản mua phải từ 1 đến 90 ngày.");
            RuleFor(x => x.PurchaseStaleBalanceDays).InclusiveBetween(1, 60).WithMessage("Số ngày coi số dư là cũ phải từ 1 đến 60.");
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

    private sealed class RefundValidator : AbstractValidator<RefundSettings>
    {
        public RefundValidator()
        {
            RuleFor(x => x.MatchWindowDays).InclusiveBetween(1, 365).WithMessage("Cửa sổ gợi ý hoàn tiền phải từ 1 đến 365 ngày.");
            RuleFor(x => x.ReversalMatchWindowDays).InclusiveBetween(1, 60).WithMessage("Cửa sổ gợi ý đảo giao dịch phải từ 1 đến 60 ngày.");
            RuleFor(x => x.MaxSuggestions).InclusiveBetween(1, 10).WithMessage("Số khoản gợi ý phải từ 1 đến 10.");
        }
    }

    private sealed class DuplicateValidator : AbstractValidator<DuplicateSettings>
    {
        public DuplicateValidator()
        {
            RuleFor(x => x.NotificationWindowMinutes).InclusiveBetween(1, 60).WithMessage("Cửa sổ trùng giữa hai thông báo phải từ 1 đến 60 phút.");
            RuleFor(x => x.ManualWindowHours).InclusiveBetween(1, 168).WithMessage("Cửa sổ trùng giữa khoản nhập tay và thông báo phải từ 1 đến 168 giờ.");
            RuleFor(x => x.ReceiptWindowHours).InclusiveBetween(1, 72).WithMessage("Cửa sổ trùng của hoá đơn phải từ 1 đến 72 giờ.");
        }
    }

    private sealed class TransferValidator : AbstractValidator<TransferSettings>
    {
        public TransferValidator()
        {
            RuleFor(x => x.PairWindowMinutes).InclusiveBetween(1, 240).WithMessage("Cửa sổ ghép cặp chuyển ví phải từ 1 đến 240 phút.");
            RuleFor(x => x.ClockSkewMinutes).InclusiveBetween(0, 10).WithMessage("Độ lệch giờ cho phép phải từ 0 đến 10 phút.");
            RuleFor(x => x.MaxFeeCents).InclusiveBetween(0, 200_000).WithMessage("Phí chuyển tối đa phải từ 0 đến 200.000 đồng.");
            RuleFor(x => x.MaxFeePercent).InclusiveBetween(0, 10).WithMessage("Phí chuyển tối đa phải từ 0 đến 10%.");
            RuleFor(x => x.ProviderAliases).NotNull()
                .Must(map => map.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null && p.Value.All(a => a is not null && a.Trim().Length >= 2)))
                .WithMessage("Mỗi bí danh của ví phải có ít nhất 2 ký tự.");
        }
    }

    private sealed class ListenerHealthValidator : AbstractValidator<ListenerHealthSettings>
    {
        public ListenerHealthValidator()
        {
            RuleFor(x => x.HistoryRetentionDays).InclusiveBetween(7, 365).WithMessage("Thời hạn lưu trạng thái thiết bị phải từ 7 đến 365 ngày.");
            RuleFor(x => x.HistoryDays).InclusiveBetween(7, 90).WithMessage("Cửa sổ lịch sử thông báo phải từ 7 đến 90 ngày.");
            RuleFor(x => x.MinNotificationsForBaseline).InclusiveBetween(2, 1000).WithMessage("Số thông báo tối thiểu để xét im lặng phải từ 2 đến 1000.");
            RuleFor(x => x.MinSilenceHours).InclusiveBetween(1, 720).WithMessage("Khoảng im lặng tối thiểu phải từ 1 đến 720 giờ.");
            RuleFor(x => x.SilenceMultiplier).InclusiveBetween(1, 20).WithMessage("Hệ số im lặng phải từ 1 đến 20.");
            RuleFor(x => x.StaleReportHours).InclusiveBetween(12, 720).WithMessage("Thời gian coi thiết bị là không rõ trạng thái phải từ 12 đến 720 giờ.");
            RuleFor(x => x.CoverageInheritHours).InclusiveBetween(12, 72).WithMessage("Thời gian kế thừa trạng thái cho ngày không báo phải từ 12 đến 72 giờ.");
            RuleFor(x => x.CooldownDays).InclusiveBetween(1, 30).WithMessage("Khoảng cách giữa hai lần nhắc phải từ 1 đến 30 ngày.");
            RuleFor(x => x.MaxRemindersPerEpisode).InclusiveBetween(1, 10).WithMessage("Số lần nhắc tối đa mỗi đợt phải từ 1 đến 10.");
            RuleFor(x => x.MaxDevicesPerUser).InclusiveBetween(1, 50).WithMessage("Số thiết bị tối đa mỗi người dùng phải từ 1 đến 50.");
            RuleFor(x => x.FormatDriftRatio).InclusiveBetween(1.1, 100).WithMessage("Hệ số tăng tỉ lệ đọc lỗi phải từ 1,1 đến 100.");
            RuleFor(x => x.FormatDriftMinSamples).InclusiveBetween(1, 10_000).WithMessage("Số mẫu tối thiểu phải từ 1 đến 10.000.");
            RuleFor(x => x.FormatDriftMinRatePercent).InclusiveBetween(0, 100).WithMessage("Tỉ lệ đọc lỗi tối thiểu phải từ 0 đến 100%.");
            RuleFor(x => x.FormatDriftBaselineFloorPercent).InclusiveBetween(0, 100).WithMessage("Sàn tỉ lệ nền phải từ 0 đến 100%.");
            RuleFor(x => x.FormatDriftRecoveryRuns).InclusiveBetween(1, 168).WithMessage("Số lượt bình thường để tự đóng sự cố phải từ 1 đến 168.");
            RuleFor(x => x.FormatFinancialShareDropRatio).InclusiveBetween(0.01, 1).WithMessage("Hệ số tụt tỉ lệ giao dịch phải từ 0,01 đến 1.");
        }
    }

    private sealed class SnapshotValidator : AbstractValidator<SnapshotSettings>
    {
        public SnapshotValidator()
        {
            RuleFor(x => x.RetentionDays).InclusiveBetween(30, 3650).WithMessage("Thời hạn lưu ảnh chụp phải từ 30 đến 3650 ngày.");
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

    private sealed class NotificationPipelineValidator : AbstractValidator<NotificationPipelineSettings>
    {
        public NotificationPipelineValidator()
        {
            RuleFor(x => x.BusinessDuplicateWindowMinutes).InclusiveBetween(1, 60)
                .WithMessage("Cửa sổ xét trùng nghiệp vụ phải từ 1 đến 60 phút.");
            RuleFor(x => x.SelfTransferMergeWindowMinutes).InclusiveBetween(1, 60)
                .WithMessage("Cửa sổ gộp chuyển khoản nội bộ phải từ 1 đến 60 phút.");
        }
    }

    private sealed class ThemeValidator : AbstractValidator<ThemeSettings>
    {
        private const int MaxFonts = 30;

        public ThemeValidator()
        {
            RuleFor(x => x.PrimaryColor).Must(BeColorOrEmpty).WithMessage("Màu chủ đạo phải là mã HEX 6 số (vd #2E7D6B).");
            RuleFor(x => x.BackgroundColor).Must(BeColorOrEmpty).WithMessage("Màu nền phải là mã HEX 6 số (vd #FFFFFF).");
            RuleFor(x => x.NavBarColor).Must(BeColorOrEmpty).WithMessage("Màu thanh điều hướng phải là mã HEX 6 số.");
            RuleFor(x => x.FontFamily).Must(font => font is null || FontName.IsMatch(font))
                .WithMessage("Tên font chỉ gồm chữ, số và dấu cách (tối đa 40 ký tự).");
            RuleFor(x => x.AvailableFonts).NotNull().Must(fonts => fonts.Count <= MaxFonts)
                .WithMessage($"Danh sách font tối đa {MaxFonts} font.");
            RuleForEach(x => x.AvailableFonts).Must(font => font is not null && FontName.IsMatch(font))
                .WithMessage("Tên font chỉ gồm chữ, số và dấu cách (tối đa 40 ký tự), đúng như trên Google Fonts.");
            RuleFor(x => x.AvailableFonts).Must(fonts => fonts.Distinct(StringComparer.OrdinalIgnoreCase).Count() == fonts.Count)
                .When(x => x.AvailableFonts is not null).WithMessage("Danh sách font có tên bị trùng.");
            RuleFor(x => x.FontFamily)
                .Must((theme, font) => font is null || font == ThemeSettings.DefaultFont || (theme.AvailableFonts?.Contains(font) ?? false))
                .When(x => x.FontFamily is not null && FontName.IsMatch(x.FontFamily))
                .WithMessage("Font đang chọn không còn trong danh sách font. Chọn lại font hoặc thêm font đó vào danh sách.");
            RuleFor(x => x.TextScale).InclusiveBetween(0.8, 1.5).WithMessage("Hệ số cỡ chữ phải từ 0,8 đến 1,5.");
            RuleFor(x => x.LogoUrl).Must(url => url is null || (url.Length <= 500 && IsHttps(url)))
                .WithMessage("Đường dẫn logo phải là địa chỉ https (tối đa 500 ký tự).");
        }

        private static bool BeColorOrEmpty(string? color) => color is null || HexColor.IsMatch(color);
    }
}
