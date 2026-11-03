namespace FinMate.Domain.Enums;

/// <summary>Mức độ bất thường (BR-11.3): chỉ <see cref="High"/> mới gửi thông báo đẩy; <see cref="Normal"/> chỉ đánh dấu trong app.</summary>
public enum AnomalySeverity
{
    Normal,
    High,
}
