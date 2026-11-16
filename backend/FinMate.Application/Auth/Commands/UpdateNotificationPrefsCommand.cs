namespace FinMate.Application.Auth.Commands;

/// <param name="ListenerHealthAlertsEnabled">Phase 84: nhắc khi app không còn đọc được thông báo ngân hàng. Null = giữ nguyên (app cũ không gửi trường này).</param>
public record UpdateNotificationPrefsCommand(
    Guid UserId,
    bool PushEnabled,
    bool BudgetAlertsEnabled,
    bool? ListenerHealthAlertsEnabled = null);
