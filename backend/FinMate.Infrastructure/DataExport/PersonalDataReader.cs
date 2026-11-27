using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.DataExport;
using FinMate.Application.DataExport.Package;
using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.DataExport;

/// <summary>
/// Đọc gói dữ liệu cá nhân theo danh mục đã duyệt (kế hoạch Phase 91 mục 2). Mỗi file một phương thức, mỗi trường được chiếu tường minh:
/// không bao giờ serialize entity, để cột mới (có thể là bí mật) không tự lọt vào gói. Mọi truy vấn gốc lọc theo <c>userId</c>; dữ liệu của người khác
/// (thành viên nhóm, nhân viên) chỉ còn vai trò. <c>IgnoreQueryFilters</c> có chủ ý: bản đã xoá mềm vẫn là dữ liệu đang giữ (D6).
/// </summary>
public sealed partial class PersonalDataReader(FinMateDbContext db) : IPersonalDataReader
{
    /// <summary>D4: chỉ các sự kiện này của nhật ký audit vào gói, và không kèm metadata.</summary>
    private static readonly Dictionary<string, string> SecurityEvents = new()
    {
        [AuditEvents.UserRegistered] = "Đăng ký tài khoản",
        [AuditEvents.LoginSuccess] = "Đăng nhập",
        [AuditEvents.LoginFailed] = "Đăng nhập thất bại",
        [AuditEvents.GoogleLoginSuccess] = "Đăng nhập bằng Google",
        [AuditEvents.GoogleLinked] = "Liên kết Google",
        [AuditEvents.GoogleRegistered] = "Đăng ký bằng Google",
        [AuditEvents.PasswordChanged] = "Đổi mật khẩu",
        [AuditEvents.PasswordReset] = "Đặt lại mật khẩu",
        [AuditEvents.EmailVerified] = "Xác minh email",
        [AuditEvents.LogoutAllDevices] = "Đăng xuất mọi thiết bị",
        [AuditEvents.TokenReuseDetected] = "Phát hiện phiên đăng nhập bị dùng lại",
        [AuditEvents.AccountDeletionRequested] = "Yêu cầu xoá tài khoản",
        [AuditEvents.ConsentChanged] = "Thay đổi đồng ý",
        [AuditEvents.DataExportRequested] = "Yêu cầu tải dữ liệu",
        [AuditEvents.DataExportReady] = "Gói dữ liệu sẵn sàng",
        [AuditEvents.DataExportFailed] = "Tạo gói dữ liệu thất bại",
        [AuditEvents.DataExportLinkIssued] = "Lấy link tải dữ liệu",
        [AuditEvents.DataExportDownloaded] = "Tải gói dữ liệu",
        [AuditEvents.DataExportExpired] = "Gói dữ liệu hết hạn",
        [AuditEvents.DataExportTransactionsCsv] = "Xuất giao dịch (CSV)",
    };

    public async Task<IReadOnlyList<PackageSection>> ReadSectionsAsync(Guid userId, CancellationToken ct = default)
    {
        var sections = new List<PackageSection>();
        sections.AddRange(await AccountSectionsAsync(userId, ct));
        sections.AddRange(await MoneySectionsAsync(userId, ct));
        sections.AddRange(await PlanningSectionsAsync(userId, ct));
        sections.Add(await GroupsAsync(userId, ct));
        sections.AddRange(await SupportAndCommunityAsync(userId, ct));
        return sections;
    }

    private async Task<IEnumerable<PackageSection>> AccountSectionsAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var role = await db.UserRoleAssignments.Where(r => r.UserId == userId).Select(r => (Domain.Enums.UserRole?)r.RoleId).FirstOrDefaultAsync(ct);
        var profile = new
        {
            id = user.Id, email = user.Email, displayName = user.DisplayName, finMateId = user.FinMateId, role,
            signInWithPassword = user.PasswordHash != null, signInWithGoogle = user.GoogleId != null,
            monthlyIncomeCents = user.MonthlyIncomeCents, forecastReserveCents = user.ForecastReserveCents, lowBalanceThresholdCents = user.LowBalanceThresholdCents,
            quietHoursStartMinute = user.QuietHoursStartMinute, quietHoursEndMinute = user.QuietHoursEndMinute, incomeProfileOverride = user.IncomeProfileOverride,
            emailVerifiedAt = user.EmailVerifiedAt, onboardingStatus = user.OnboardingStatus, onboardingCompletedAt = user.OnboardingCompletedAt,
            isLocked = user.IsLocked, createdAt = user.CreatedAt, updatedAt = user.UpdatedAt, deletedAt = user.DeletedAt,
            community = new
            {
                provinceCode = user.CommunityProvinceCode, areaCode = user.CommunityAreaCode, promptsEnabled = user.CommunityPromptsEnabled,
                mutedUntil = user.CommunityMutedUntil,
            },
        };
        var prefs = user.NotificationPrefs;
        var notificationSettings = new
        {
            pushEnabled = prefs.PushEnabled, budgetAlertsEnabled = prefs.BudgetAlertsEnabled, forecastAlertsEnabled = prefs.ForecastAlertsEnabled,
            listenerHealthAlertsEnabled = prefs.ListenerHealthAlertsEnabled, loanRemindersEnabled = prefs.LoanRemindersEnabled,
            alertT1Pct = prefs.AlertT1Pct, alertT2Pct = prefs.AlertT2Pct,
        };

        return
        [
            new PackageSection("profile.json", profile, IsList: false),
            new PackageSection("notification_settings.json", notificationSettings, IsList: false),
            new PackageSection("consents.json", await ConsentsAsync(user, ct), IsList: false),
            new PackageSection("devices.json", await DevicesAsync(userId, ct), IsList: false),
            new PackageSection("account_requests.json", await AccountRequestsAsync(userId, ct), IsList: false),
            new PackageSection("security_log.json", await SecurityLogAsync(userId, ct), IsList: true),
            new PackageSection("notifications.json", await NotificationsAsync(userId, ct), IsList: false),
        ];
    }

    /// <summary>D3: trạng thái hiện tại từng loại (từ cột trên <c>users</c>), kèm lịch sử từ audit <c>User.ConsentChanged</c> (có từ 2026-11-25).</summary>
    private async Task<object> ConsentsAsync(Domain.Entities.User user, CancellationToken ct)
    {
        var history = await db.AuditLogs.AsNoTracking().Where(a => a.UserId == user.Id && a.EventType == AuditEvents.ConsentChanged)
            .OrderBy(a => a.CreatedAt).Select(a => new { a.Metadata, a.CreatedAt }).ToListAsync(ct);
        return new
        {
            current = new object[]
            {
                new { type = "terms", version = user.TermsVersion, at = user.TermsAcceptedAt },
                new { type = "ai", version = user.AiConsentVersion, at = user.AiConsentAt },
                new { type = "training", version = user.TrainingConsentVersion, at = user.TrainingConsentAt },
                new { type = "survey_ai", version = user.SurveyAiConsentVersion, at = user.SurveyAiConsentAt },
                new { type = "community", version = user.CommunityConsentVersion, at = user.CommunityConsentAt },
            },
            history = history.Select(h => new { change = ParseJson(h.Metadata), at = h.CreatedAt }).ToList(),
        };
    }

    private async Task<object> DevicesAsync(Guid userId, CancellationToken ct) => new
    {
        pushDevices = await db.DeviceTokens.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.CreatedAt)
            .Select(d => new { platform = d.Platform, createdAt = d.CreatedAt, lastSeenAt = d.LastSeenAt }).ToListAsync(ct),
        listener = await db.DeviceListenerStatuses.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.FirstReportedAt)
            .Select(s => new
            {
                installationId = s.InstallationId, permissionGranted = s.PermissionGranted, listenerConnected = s.ListenerConnected,
                selectedPackages = s.SelectedPackages, appVersion = s.AppVersion, manufacturer = s.Manufacturer, permissionLostSince = s.PermissionLostSince,
                firstReportedAt = s.FirstReportedAt, lastReportedAt = s.LastReportedAt,
            }).ToListAsync(ct),
        listenerDays = await db.DeviceListenerDays.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.Day)
            .Select(d => new
            {
                installationId = d.Status!.InstallationId, day = d.Day, anyPermissionDenied = d.AnyPermissionDenied,
                lastPermissionGranted = d.LastPermissionGranted, lastListenerConnected = d.LastListenerConnected, reportCount = d.ReportCount,
            }).ToListAsync(ct),
    };

    private async Task<object> AccountRequestsAsync(Guid userId, CancellationToken ct) => new
    {
        deletionRequests = await db.DataDeletionRequests.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.RequestedAt)
            .Select(r => new { requestedAt = r.RequestedAt, scheduledHardDeleteAt = r.ScheduledHardDeleteAt, status = r.Status, processedAt = r.ProcessedAt })
            .ToListAsync(ct),
        dataExports = (await db.PersonalDataExports.AsNoTracking().Where(e => e.UserId == userId).OrderBy(e => e.RequestedAt).ToListAsync(ct))
            .Select(PersonalDataExportDto.From).ToList(),
    };

    private async Task<object> SecurityLogAsync(Guid userId, CancellationToken ct)
    {
        var events = SecurityEvents.Keys.ToArray();
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.UserId == userId && events.Contains(a.EventType)).OrderBy(a => a.CreatedAt)
            .Select(a => new { a.EventType, a.CreatedAt, a.IpAddress }).ToListAsync(ct);
        return rows.Select(r => new { eventType = r.EventType, description = SecurityEvents[r.EventType], at = r.CreatedAt, ipAddress = r.IpAddress }).ToList();
    }

    private async Task<object> NotificationsAsync(Guid userId, CancellationToken ct) => new
    {
        inbox = await db.UserNotifications.AsNoTracking().Where(n => n.UserId == userId).OrderBy(n => n.CreatedAt)
            .Select(n => new
            {
                type = n.Type, title = n.Title, body = n.Body, relatedEntityType = n.RelatedEntityType, relatedEntityId = n.RelatedEntityId,
                createdAt = n.CreatedAt, readAt = n.ReadAt,
            }).ToListAsync(ct),
        forecastAlerts = await db.ForecastAlerts.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                kind = a.Kind, title = a.Title, body = a.Body, status = a.Status, scheduledFor = a.ScheduledFor, sentAt = a.SentAt,
                markedInaccurate = a.MarkedInaccurate, feedbackAt = a.FeedbackAt, createdAt = a.CreatedAt,
            }).ToListAsync(ct),
    };

    /// <summary>Cột jsonb/chuỗi JSON thành phần tử JSON (để không bị ghi thành chuỗi có dấu \"). Hỏng thì giữ nguyên chuỗi.</summary>
    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
