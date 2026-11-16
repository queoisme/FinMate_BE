using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Notifications;

/// <summary>
/// Đọc gợi ý quan hệ của AI Service thành giá trị lưu ở <c>ai_results</c>. Hoàn/đảo chỉ có nghĩa với tiền vào (Phase 81); chuyển ví có nghĩa với
/// cả hai chiều (Phase 83, D1). Giá trị lạ (hợp đồng AI mới hơn backend) bị bỏ qua.
/// </summary>
public static class NotificationHints
{
    public static (NotificationRelationHint? Hint, TransferKind? Kind) Parse(ExtractionResult? extraction, TransactionType? type)
    {
        var hint = extraction?.RelationHint?.ToLowerInvariant() switch
        {
            "refund" when type == TransactionType.Credit => NotificationRelationHint.Refund,
            "reversal" when type == TransactionType.Credit => NotificationRelationHint.Reversal,
            "transfer" when type is TransactionType.Debit or TransactionType.Credit => NotificationRelationHint.Transfer,
            _ => (NotificationRelationHint?)null,
        };
        var kind = hint == NotificationRelationHint.Transfer ? TransferKindColumn.Parse(extraction?.TransferKind) : null;
        return (hint, kind);
    }
}
