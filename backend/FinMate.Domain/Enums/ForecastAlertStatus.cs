namespace FinMate.Domain.Enums;

/// <summary><see cref="Pending"/>: đang chờ gửi (bị hoãn vì giờ yên lặng). <see cref="Sent"/>: đã giao cho kênh push.</summary>
public enum ForecastAlertStatus
{
    Pending,
    Sent,
}
