using System.Text;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Monitoring;

public interface IIncidentNotifier
{
    /// <summary>Gửi các sự cố đến hạn báo hoặc nhắc lại, và báo các sự cố vừa đóng.</summary>
    Task SendDueAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Gom các sự cố mức thường vào một email cho Admin.</summary>
    Task SendDigestAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Email chỉ có dịch vụ, mức độ, ảnh hưởng và thời điểm, không có chi tiết kỹ thuật. Không đánh dấu đã báo nếu không ai nhận được, để lượt sau thử lại.
/// </summary>
public sealed class IncidentNotifier : IIncidentNotifier
{
    /// <summary>Số lần báo trong giờ đầu ở lịch dày. Sau đó giãn còn mỗi giờ một lần, để sự cố kéo dài không thành hàng chục email.</summary>
    private static readonly TimeSpan DenseReminderPeriod = TimeSpan.FromHours(1);
    private static readonly TimeSpan SparseReminderInterval = TimeSpan.FromHours(1);

    private readonly IMonitoringRepository _repository;
    private readonly IEmailSender _email;
    private readonly ILogger<IncidentNotifier> _logger;

    public IncidentNotifier(IMonitoringRepository repository, IEmailSender email, ILogger<IncidentNotifier> logger)
    {
        _repository = repository;
        _email = email;
        _logger = logger;
    }

    public async Task SendDueAsync(MonitoringSettings settings, DateTimeOffset now, CancellationToken ct = default)
    {
        var due = (await _repository.ListOpenUrgentAsync(ct)).Where(i => IsDue(i, settings, now)).ToList();
        var resolved = await _repository.ListResolvedUnnotifiedAsync(ct);
        if (due.Count == 0 && resolved.Count == 0)
        {
            return;
        }

        var recipients = await _repository.ListAlertRecipientsAsync(includeManagers: true, ct);
        foreach (var incident in due)
        {
            var isReminder = incident.NotificationCount > 0;
            var subject = $"[FinMate] {SeverityLabel(incident.Severity)}: {incident.Title}" + (isReminder ? " (chưa có người nhận)" : string.Empty);
            if (await SendAsync(recipients, subject, Body(incident), ct))
            {
                incident.LastNotifiedAt = now;
                incident.NotificationCount++;
            }
        }

        foreach (var incident in resolved)
        {
            if (await SendAsync(recipients, $"[FinMate] Đã khắc phục: {incident.Title}", ResolvedBody(incident), ct))
            {
                incident.ResolvedNotifiedAt = now;
            }
        }

        await _repository.SaveChangesAsync(ct);
    }

    public async Task SendDigestAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var incidents = await _repository.ListOpenNormalAsync(ct);
        if (incidents.Count == 0)
        {
            return;
        }

        var recipients = await _repository.ListAlertRecipientsAsync(includeManagers: false, ct);
        var body = new StringBuilder("Các sự cố mức thường đang mở:\n\n");
        foreach (var incident in incidents)
        {
            body.Append("- ").Append(incident.Title).Append(" (từ ").Append(Format(incident.FirstSeenAt)).Append(")\n");
        }

        body.Append("\nXem chi tiết trên trang vận hành.");
        if (await SendAsync(recipients, $"[FinMate] {incidents.Count} sự cố mức thường đang mở", body.ToString(), ct))
        {
            foreach (var incident in incidents)
            {
                incident.LastNotifiedAt = now;
                incident.NotificationCount++;
            }

            await _repository.SaveChangesAsync(ct);
        }
    }

    internal static bool IsDue(OpsIncident incident, MonitoringSettings settings, DateTimeOffset now)
    {
        if (incident.LastNotifiedAt is not { } last)
        {
            return true;
        }

        var interval = now - incident.FirstSeenAt <= DenseReminderPeriod
            ? TimeSpan.FromMinutes(settings.ReminderMinutes)
            : SparseReminderInterval;
        return now - last >= interval;
    }

    private async Task<bool> SendAsync(IReadOnlyList<AlertRecipient> recipients, string subject, string body, CancellationToken ct)
    {
        if (recipients.Count == 0)
        {
            _logger.LogWarning("Có sự cố nhưng không có tài khoản nào để nhận cảnh báo");
            return false;
        }

        var delivered = 0;
        foreach (var recipient in recipients)
        {
            if (await _email.SendAsync(recipient.Email, subject, body, ct))
            {
                delivered++;
            }
        }

        // Không ai nhận được thì log mức Error: đây là dấu vết duy nhất khi Brevo hỏng, vì email là kênh báo duy nhất.
        if (delivered == 0)
        {
            _logger.LogError("Không gửi được cảnh báo sự cố cho ai trong {Total} người nhận", recipients.Count);
        }
        else if (delivered < recipients.Count)
        {
            _logger.LogWarning("Không gửi được cảnh báo sự cố cho {Failed}/{Total} người nhận", recipients.Count - delivered, recipients.Count);
        }

        return delivered > 0;
    }

    private static string Body(OpsIncident incident)
        => $"""
           Mức độ: {SeverityLabel(incident.Severity)}
           Sự cố: {incident.Title}
           Ảnh hưởng: {MonitoredServices.ImpactOf(incident.Service)}
           Bắt đầu: {Format(incident.FirstSeenAt)}

           Mở trang vận hành để nhận xử lý. Hệ thống sẽ nhắc lại cho tới khi có người nhận.
           """;

    private static string ResolvedBody(OpsIncident incident)
        => $"""
           Sự cố: {incident.Title}
           Bắt đầu: {Format(incident.FirstSeenAt)}
           Kết thúc: {Format(incident.ResolvedAt ?? incident.LastSeenAt)}
           """;

    private static string SeverityLabel(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical => "Nghiêm trọng",
        IncidentSeverity.High => "Cao",
        _ => "Thường",
    };

    private static string Format(DateTimeOffset at) => VietnamTime.ToLocal(at).ToString("HH:mm dd/MM/yyyy") + " (giờ VN)";
}
