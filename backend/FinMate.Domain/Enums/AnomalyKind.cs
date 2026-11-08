namespace FinMate.Domain.Enums;

/// <summary>Loại bất thường. Cột TEXT dạng snake_case.</summary>
public enum AnomalyKind
{
    /// <summary>Cao hơn hẳn mức thường chi cho danh mục.</summary>
    CategorySpike,

    /// <summary>Cao hơn hẳn mức thường trả cho cùng người nhận / cửa hàng.</summary>
    MerchantSpike,

    /// <summary>Cùng ví, cùng số tiền, cùng nơi nhận trong vài phút — có thể bị trừ hai lần.</summary>
    Duplicate,
}
