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
    /// <summary>Phần trăm ngân sách đã dùng để báo "sắp chạm".</summary>
    public int BudgetNearPercent { get; init; } = 80;
    public int BudgetOverPercent { get; init; } = 100;
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

/// <summary>Toàn bộ cấu hình hệ thống không bí mật. Giá trị mặc định ở đây là cấu hình khi chưa ai lưu (version 0).</summary>
public sealed record SystemConfigDocument
{
    public MaintenanceSettings Maintenance { get; init; } = new();
    public AppVersionSettings AppVersion { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();
    public LimitSettings Limits { get; init; } = new();
    public SyncSettings Sync { get; init; } = new();
    public ThemeSettings Theme { get; init; } = new();
}

/// <summary>Cấu hình đang chạy kèm số phiên bản (0 = chưa ai lưu, đang dùng giá trị mặc định).</summary>
public sealed record SystemConfigSnapshot(int Version, SystemConfigDocument Document, DateTimeOffset? UpdatedAt);
