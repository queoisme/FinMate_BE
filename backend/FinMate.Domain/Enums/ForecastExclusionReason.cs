namespace FinMate.Domain.Enums;

/// <summary>Vì sao một giao dịch không được dùng làm dữ liệu dự báo.</summary>
public enum ForecastExclusionReason
{
    /// <summary>Khoản chi một lần (đãi tiệc, mua laptop): có thật nhưng không phải thói quen.</summary>
    OneOff,

    /// <summary>Người dùng tự loại, không nêu lý do.</summary>
    UserMarked,
}
