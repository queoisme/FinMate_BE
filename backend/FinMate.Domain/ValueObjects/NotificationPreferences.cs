namespace FinMate.Domain.ValueObjects;

public class NotificationPreferences
{
    public bool PushEnabled { get; set; } = true;
    public bool BudgetAlertsEnabled { get; set; } = true;
    public bool MissionRemindersEnabled { get; set; } = true;

    /// <summary>
    /// Ngưỡng cảnh báo ngân sách CÁ NHÂN (T1 nhắc nhẹ, T2 cảnh báo mạnh, % hạn mức đã dùng). Cả hai cùng null = dùng mặc định hệ thống. Chỉ có tác dụng khi người dùng
    /// không đang dùng cách quản lý nào định nghĩa T1/T2 (cách quản lý có ngưỡng riêng thì theo cách đó). Ràng buộc 1 ≤ T1 &lt; T2 ≤ 99 do DB và service cùng kiểm.
    /// </summary>
    public int? AlertT1Pct { get; set; }

    public int? AlertT2Pct { get; set; }
}
