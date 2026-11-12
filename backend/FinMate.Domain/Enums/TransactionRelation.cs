namespace FinMate.Domain.Enums;

/// <summary>
/// Quan hệ của một khoản tiền VÀO với một khoản chi trước đó (Phase 81, FR-TXN-18). Khoản mang quan hệ luôn là
/// <see cref="TransactionType.Credit"/> (số dư +) nhưng KHÔNG phải thu nhập: nó làm giảm chi ròng của danh mục khoản gốc.
/// </summary>
public enum TransactionRelation
{
    /// <summary>Hoàn tiền (một phần hoặc toàn bộ). Ghi nhận ở kỳ của ngày nhận tiền hoàn. Có thể "mồ côi" (không có khoản gốc trong app).</summary>
    Refund,

    /// <summary>Đảo giao dịch lỗi. Khoản gốc bị gắn <c>IsReversed</c>, cả cặp bị loại khỏi mọi tổng hợp, kể cả ở kỳ của khoản gốc.</summary>
    Reversal,
}
