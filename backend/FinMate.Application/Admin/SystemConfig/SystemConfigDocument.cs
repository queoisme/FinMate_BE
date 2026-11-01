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
}

/// <summary>Cấu hình đang chạy kèm số phiên bản (0 = chưa ai lưu, đang dùng giá trị mặc định).</summary>
public sealed record SystemConfigSnapshot(int Version, SystemConfigDocument Document, DateTimeOffset? UpdatedAt);
