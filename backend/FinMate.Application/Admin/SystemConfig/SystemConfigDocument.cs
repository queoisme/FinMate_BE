namespace FinMate.Application.Admin.SystemConfig;

/// <summary>Tên các tài liệu cấu hình hệ thống (cột <c>name</c> của <c>system_config_versions</c>).</summary>
public static class SystemConfigNames
{
    public const string App = "app";
}

public sealed record MaintenanceSettings
{
    public bool Enabled { get; init; }
    public string Title { get; init; } = "FinMate đang bảo trì";
    public string Message { get; init; } = "Chúng tôi đang nâng cấp hệ thống để phục vụ bạn tốt hơn. Vui lòng quay lại sau ít phút.";
    public DateTimeOffset? EstimatedEndAt { get; init; }
}

public sealed record AppVersionSettings
{
    /// <summary>Phiên bản app thấp hơn mức này phải cập nhật mới dùng được.</summary>
    public string MinVersion { get; init; } = "1.0.0";
    public string LatestVersion { get; init; } = "1.0.0";

    /// <summary>Bật thì app cũ hơn <see cref="LatestVersion"/> bị buộc cập nhật (không chỉ dưới <see cref="MinVersion"/>).</summary>
    public bool ForceUpdate { get; init; }
    public string StoreUrl { get; init; } = string.Empty;
}

public sealed record FeatureSettings
{
    public bool ChatAi { get; init; } = true;
    public bool Voice { get; init; } = true;
    public bool BillScan { get; init; } = true;
    public bool NotificationReading { get; init; } = true;

    /// <summary>Tài chính nhóm. Mặc định tắt: chỉ bật sau khi app đã có màn hình và người dùng đã đồng ý Điều khoản mới.</summary>
    public bool Groups { get; init; }

    /// <summary>Ticket hỗ trợ cho khách. Mặc định tắt: chỉ bật sau khi app có màn hình và web vận hành đã có xác thực 2 lớp.</summary>
    public bool Support { get; init; }
}

public sealed record GroupSettings
{
    public int MaxMembers { get; init; } = 20;
    public int MaxGroupsPerUser { get; init; } = 5;
    public int MaxPendingInvitations { get; init; } = 20;
    public int InvitationTtlDays { get; init; } = 7;
}

/// <summary>Hạn SLA tính bằng phút kể từ lúc tạo ticket, và các giới hạn của ticket.</summary>
public sealed record SupportSettings
{
    public int P1FirstResponseMinutes { get; init; } = 15;
    public int P1ResolveMinutes { get; init; } = 240;
    public int P2FirstResponseMinutes { get; init; } = 60;
    public int P2ResolveMinutes { get; init; } = 1440;
    public int P3FirstResponseMinutes { get; init; } = 240;
    public int P3ResolveMinutes { get; init; } = 4320;
    public int MaxOpenTicketsPerUser { get; init; } = 10;
    public int MaxAttachmentsPerMessage { get; init; } = 3;

    /// <summary>Ca đã giải quyết mà khách không phản hồi sau chừng này ngày thì tự đóng.</summary>
    public int AutoCloseResolvedDays { get; init; } = 7;

    /// <summary>Ảnh trong ticket bị xóa sau khi ca đóng chừng này ngày.</summary>
    public int AttachmentRetentionDays { get; init; } = 30;
}

/// <summary>Ngưỡng giám sát hệ thống. Mức độ của từng dịch vụ nằm trong code, xem <c>MonitoredServices</c>.</summary>
public sealed record MonitoringSettings
{
    /// <summary>Số lần kiểm tra lỗi liên tiếp mới tính là sự cố, để một lần chập chờn không thành cảnh báo.</summary>
    public int ProbeFailureThreshold { get; init; } = 3;
    public int ProbeRecoveryThreshold { get; init; } = 3;
    public int ErrorCountThreshold { get; init; } = 10;
    public int ErrorWindowMinutes { get; init; } = 5;

    /// <summary>Lỗi không lặp lại trong chừng này phút thì sự cố tự đóng.</summary>
    public int ErrorQuietMinutes { get; init; } = 30;
    public int ProviderFailureThreshold { get; init; } = 3;
    public int ReminderMinutes { get; init; } = 15;
    public int ErrorRetentionDays { get; init; } = 30;
}

public sealed record LimitSettings
{
    /// <summary>
    /// Ngưỡng cảnh báo ngân sách MẶC ĐỊNH của hệ thống, tính theo % hạn mức đã dùng: T1 (nhắc nhẹ) và T2 (cảnh báo mạnh). Chỉ dùng cho người dùng KHÔNG có
    /// ngưỡng cá nhân và không đang dùng cách quản lý nào định nghĩa T1/T2; mỗi cách quản lý có T1/T2 riêng và người dùng có thể đặt ngưỡng cá nhân.
    /// Mức "vượt hạn mức" luôn là 100% nên không cấu hình ở đây. Đây là NƠI DUY NHẤT giữ hai con số mặc định (70, 90).
    /// </summary>
    public int AlertT1Percent { get; init; } = 70;
    public int AlertT2Percent { get; init; } = 90;
}

public sealed record SyncSettings
{
    public int IntervalMinutes { get; init; } = 15;
    public int MaxRetries { get; init; } = 5;
    public int QueueRetentionDays { get; init; } = 7;
}

/// <summary>Giao diện app. Giá trị <c>null</c> nghĩa là "dùng mặc định có sẵn trong app", nên cấu hình trống không đổi gì trên máy người dùng.</summary>
public sealed record ThemeSettings
{
    public string? PrimaryColor { get; init; }
    public string? BackgroundColor { get; init; }
    public string? NavBarColor { get; init; }

    /// <summary>Tên font trên Google Fonts; app không tải được thì dùng font mặc định.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Hệ số phóng cỡ chữ toàn app (1.0 = mặc định).</summary>
    public double TextScale { get; init; } = 1.0;
    public string? LogoUrl { get; init; }
}

/// <summary>
/// Quét QR chuyển khoản (VietQR). Các giá trị từng nằm cứng trong code: admin chỉnh được mà không cần phát hành lại, có lịch sử và khôi phục như mọi cấu hình khác. Mặc định ở đây là giá trị đã dùng khi chưa ai
/// lưu cấu hình, nên thêm nhóm này không đổi hành vi của bản cũ.
/// </summary>
public sealed record QrTransferSettings
{
    /// <summary>Thông báo ngân hàng phải đến trong vòng này SAU lúc quét thì mới khớp với lần quét. Quá hẹp thì lỡ người dùng nhập OTP chậm, quá rộng thì khớp nhầm giao dịch cùng số tiền.</summary>
    public int MatchWindowMinutes { get; init; } = 30;

    /// <summary>Đồng hồ điện thoại (gắn vào thông báo) và máy chủ (gắn vào lúc quét) có thể lệch: cho phép lần quét "sau" thông báo tối đa chừng này.</summary>
    public int ClockSkewMinutes { get; init; } = 2;

    /// <summary>Lần quét chưa khớp quá chừng này thì được đánh dấu hết hạn (chỉ để hiển thị).</summary>
    public int PendingLifetimeHours { get; init; } = 24;

    /// <summary>Trần số tiền một lần quét (đồng): chặn số rác, không phải hạn mức nghiệp vụ.</summary>
    public long MaxAmount { get; init; } = 100_000_000_000;

    /// <summary>Độ dài tối đa nội dung chuyển khoản. Cột DB rộng 100 nên không đặt quá 100.</summary>
    public int MaxMessageLength { get; init; } = 100;

    /// <summary>Nội dung chuyển khoản điền sẵn khi mã QR không có. Nên viết không dấu: nhiều ngân hàng không nhận dấu tiếng Việt.</summary>
    public string DefaultMessage { get; init; } = "Chuyen tien qua FinMate";

    /// <summary>Máy chủ chuyển tiếp mở app ngân hàng (chỉ tên miền, không kèm đường dẫn). Số tài khoản, số tiền và nội dung của người dùng đi qua máy chủ này nên chỉ đổi khi dịch vụ đổi địa chỉ.</summary>
    public string DeeplinkHost { get; init; } = "dl.vietqr.io";

    /// <summary>Tham số <c>url</c> của liên kết mở app ngân hàng. Thiếu nó thì app ngân hàng chỉ mở màn hình chủ.</summary>
    public string DeeplinkReturnUrl { get; init; } = "https://www.vietqr.io/deeplink";
}

/// <summary>
/// Gợi ý cách quản lý tài chính. Hai hằng số từng nằm cứng trong code; mặc định là giá trị đã dùng nên thêm nhóm này không đổi hành vi của bản cũ.
/// Chỉ dẫn gửi cho AI nằm ở chỗ khác (<c>/admin/finance-method-prompts</c>) vì có phiên bản riêng để truy ngược từng gợi ý.
/// </summary>
public sealed record FinanceMethodSettings
{
    /// <summary>Trong khoảng này, xin gợi ý lại với CÙNG câu trả lời khảo sát thì trả bản vừa tạo, không gọi AI lại. 0 = luôn tạo mới.</summary>
    public int RecommendationReuseMinutes { get; init; } = 10;

    /// <summary>Khi chọn bằng luật (AI lỗi hoặc không dùng được), cách bổ trợ chỉ được chọn nếu điểm của nó ít nhất bằng chừng này phần trăm điểm của cách chính.</summary>
    public int SupportMinSharePercent { get; init; } = 50;
}

/// <summary>Một khoảng ngày lễ/Tết mà chi tiêu linh hoạt thường tăng (hoặc giảm). Tết đổi ngày theo âm lịch nên admin nhập theo từng năm.</summary>
public sealed record ForecastHoliday
{
    public string Name { get; init; } = string.Empty;
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }

    /// <summary>Hệ số chi tiêu linh hoạt trong khoảng này, theo phần trăm (140 = chi nhiều hơn 40%).</summary>
    public int MultiplierPercent { get; init; } = 100;
}

/// <summary>
/// Dự báo tài chính (Flow 6). Các số ở đây thay cho những mã BR mà sơ đồ nhắc tới nhưng chưa có định nghĩa; mặc định do chủ dự án duyệt ngày 2026-11-02.
/// </summary>
public sealed record ForecastSettings
{
    /// <summary>Cửa sổ lịch sử dùng để học thói quen chi tiêu.</summary>
    public int HistoryWindowDays { get; init; } = 90;

    /// <summary>Đủ dữ liệu (BR-01.2, BR-06.1): ít nhất chừng này ngày có giao dịch trong cửa sổ lịch sử; ít hơn thì dự báo mang cờ tin cậy thấp.</summary>
    public int MinHistoryDays { get; init; } = 30;

    /// <summary>Dự phòng mặc định (BR-03.3) theo % thu nhập dự kiến tháng, khi user chưa tự đặt.</summary>
    public int ReservePercentOfIncome { get; init; } = 5;

    /// <summary>Dự báo dòng tiền xa nhất bao nhiêu ngày.</summary>
    public int MaxHorizonDays { get; init; } = 90;

    /// <summary>Số tiền các lần của cùng một khoản định kỳ được lệch nhau tối đa bao nhiêu %.</summary>
    public int AmountTolerancePercent { get; init; } = 10;

    public int MonthlyMinIntervalDays { get; init; } = 25;
    public int MonthlyMaxIntervalDays { get; init; } = 35;
    public int WeeklyMinIntervalDays { get; init; } = 6;
    public int WeeklyMaxIntervalDays { get; init; } = 8;

    /// <summary>Thấy chừng này lần lặp thì đề xuất khoản định kỳ chờ user xác nhận.</summary>
    public int SuggestMinOccurrences { get; init; } = 2;

    /// <summary>Thấy chừng này lần lặp thì tự ghi nhận (vẫn sửa/hủy được).</summary>
    public int AutoActivateOccurrences { get; init; } = 3;

    /// <summary>Khoản chi định kỳ quá hạn mà chưa thấy thanh toán vẫn được coi là sắp trả trong chừng này ngày; quá đó thì không tính nữa.</summary>
    public int OverdueGraceDays { get; init; } = 7;

    /// <summary>Số khoản giả định tối đa trong một lần thử kịch bản.</summary>
    public int MaxScenarioItems { get; init; } = 20;

    // ---- Đánh giá khoản mua ("Mua món này có ổn không?") ----

    /// <summary>Chưa biết kỳ thu nhập kế tiếp và người dùng không chọn mốc: đánh giá tới chừng này ngày sau ngày mua (kết quả chỉ ở mức một phần).</summary>
    public int PurchaseDefaultHorizonDays { get; init; } = 14;

    /// <summary>Số dư ví cập nhật cách đây quá chừng này ngày thì đánh giá chỉ ở mức một phần.</summary>
    public int PurchaseStaleBalanceDays { get; init; } = 7;

    public IReadOnlyList<ForecastHoliday> Holidays { get; init; } = [];

    // ---- Đợt 2: cảnh báo (duyệt 2026-11-02) ----

    /// <summary>Cảnh báo số dư thấp nhìn trước chừng này ngày.</summary>
    public int LowBalanceHorizonDays { get; init; } = 14;

    /// <summary>Đã gửi cảnh báo số dư thấp thì chừng này ngày sau mới gửi lại ("Đã gửi cảnh báo tương tự gần đây?").</summary>
    public int LowBalanceCooldownDays { get; init; } = 3;

    /// <summary>User báo một cảnh báo số dư thấp "không chính xác" thì tắt loại cảnh báo này chừng này ngày.</summary>
    public int InaccurateSuppressDays { get; init; } = 7;

    /// <summary>Vượt ngân sách sớm (BR-04.2): đã qua ít nhất chừng này ngày trong tháng thì dùng nhịp chi tháng này, ít hơn thì dùng thói quen 90 ngày.</summary>
    public int BudgetRiskMinElapsedDays { get; init; } = 7;

    /// <summary>Vượt ngân sách sớm chỉ báo khi còn ít nhất chừng này ngày để kịp điều chỉnh.</summary>
    public int BudgetRiskMinDaysRemaining { get; init; } = 3;

    /// <summary>Nhắc hóa đơn trước hạn chừng này ngày.</summary>
    public int BillReminderDaysBefore { get; init; } = 3;

    /// <summary>Thu nhập trễ (BR-07.4): quá ngày dự kiến chừng này ngày mà chưa thấy thì báo.</summary>
    public int IncomeLateDays { get; init; } = 3;

    /// <summary>Thu nhập không đều (BR-07.2) ước tính từ chừng này tháng gần nhất.</summary>
    public int IrregularIncomeMonths { get; init; } = 6;

    /// <summary>Giờ yên lặng mặc định (phút trong ngày, giờ VN) cho cảnh báo số dư thấp, khi user chưa đặt riêng.</summary>
    public int QuietHoursStartMinute { get; init; } = 22 * 60;
    public int QuietHoursEndMinute { get; init; } = 7 * 60;

    // ---- Đợt 3: bất thường, chi theo danh mục, mục tiêu (duyệt 2026-11-02) ----

    /// <summary>Bất thường: so với các khoản chi trong chừng này ngày gần nhất.</summary>
    public int AnomalyLookbackDays { get; init; } = 90;

    /// <summary>BR-11.1: cần ít nhất chừng này khoản chi cùng danh mục để so.</summary>
    public int AnomalyMinCategorySamples { get; init; } = 5;

    /// <summary>So theo người nhận cần ít nhất chừng này lần trả trước đó.</summary>
    public int AnomalyMinMerchantSamples { get; init; } = 3;

    /// <summary>BR-11.2: gấp ít nhất chừng này phần trăm mức thông thường (trung vị) mới là bất thường.</summary>
    public int AnomalyRatioPercent { get; init; } = 300;

    /// <summary>BR-11.3: gấp ít nhất chừng này phần trăm thì là mức cao (gửi thông báo).</summary>
    public int AnomalyHighRatioPercent { get; init; } = 500;

    /// <summary>Khoản dưới số tiền này không bao giờ bị coi là bất thường (tránh nhiễu với khoản nhỏ).</summary>
    public long AnomalyMinAmountCents { get; init; } = 200_000;

    /// <summary>Hai khoản cùng ví, cùng số tiền, cùng nơi nhận trong chừng này phút → nghi trùng.</summary>
    public int DuplicateWindowMinutes { get; init; } = 10;

    /// <summary>BR-11.4: đã xác nhận bình thường một khoản thì khoản tương tự có số tiền tới (khoản đó × chừng này %) không bị báo nữa.</summary>
    public int NormalFeedbackTolerancePercent { get; init; } = 125;

    /// <summary>Quét bất thường các khoản chi trong chừng này ngày gần nhất.</summary>
    public int AnomalyScanDays { get; init; } = 3;

    /// <summary>Dự báo theo danh mục dùng chừng này tháng trọn vẹn gần nhất.</summary>
    public int CategoryForecastMonths { get; init; } = 3;

    /// <summary>BR-06.1: cần dữ liệu ở ít nhất chừng này tháng trọn vẹn, ít hơn → độ tin cậy thấp.</summary>
    public int CategoryForecastMinMonths { get; init; } = 2;

    /// <summary>Danh mục "thay đổi nhiều" khi dự kiến lệch ít nhất chừng này phần trăm so với tháng trước.</summary>
    public int CategoryChangePercent { get; init; } = 20;

    /// <summary>Hạn mức đề xuất làm tròn lên bội số này (đồng).</summary>
    public long BudgetSuggestionRoundingCents { get; init; } = 10_000;

    /// <summary>BR-08.1: tốc độ góp mục tiêu tính từ tối đa chừng này tháng gần nhất.</summary>
    public int GoalRateMonths { get; init; } = 6;
}

/// <summary>AI Financial Agent.</summary>
public sealed record AgentSettings
{
    /// <summary>
    /// Mẫu chia ngân sách của công cụ <c>plan_budget</c>, dùng khi người dùng chưa có chi tiêu 3 tháng trọn gần nhất: chuỗi JSON <c>{"slug": phần trăm nguyên}</c>, tổng đúng 100.
    /// Mặc định RỖNG (chủ dự án chốt 2026-11-08: không đặt tỉ lệ sẵn trong code, admin nhập). Sai dạng thì công cụ coi như chưa có mẫu.
    /// </summary>
    public string BudgetTemplate { get; init; } = string.Empty;
}

/// <summary>Toàn bộ cấu hình hệ thống không bí mật. Giá trị mặc định ở đây là cấu hình khi chưa ai lưu (version 0).</summary>
public sealed record SystemConfigDocument
{
    public MaintenanceSettings Maintenance { get; init; } = new();
    public AppVersionSettings AppVersion { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();
    public LimitSettings Limits { get; init; } = new();
    public SyncSettings Sync { get; init; } = new();
    public ThemeSettings Theme { get; init; } = new();
    public QrTransferSettings QrTransfer { get; init; } = new();
    public FinanceMethodSettings FinanceMethods { get; init; } = new();
    public ForecastSettings Forecast { get; init; } = new();
    public GroupSettings Groups { get; init; } = new();
    public SupportSettings Support { get; init; } = new();
    public MonitoringSettings Monitoring { get; init; } = new();
    public AgentSettings Agent { get; init; } = new();
}

/// <summary>Cấu hình đang chạy kèm số phiên bản (0 = chưa ai lưu, đang dùng giá trị mặc định).</summary>
public sealed record SystemConfigSnapshot(int Version, SystemConfigDocument Document, DateTimeOffset? UpdatedAt);
