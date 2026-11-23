using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Notifications;

/// <summary>
/// Đọc gợi ý quan hệ của AI Service thành giá trị lưu ở <c>ai_results</c>. Hoàn/đảo chỉ có nghĩa với tiền vào (Phase 81); chuyển ví có nghĩa với
/// cả hai chiều (Phase 83, D1), trả nợ cũng vậy (Phase 90, kèm độ mạnh). Giá trị lạ (hợp đồng AI mới hơn backend) bị bỏ qua.
/// </summary>
public static class NotificationHints
{
    public static (NotificationRelationHint? Hint, TransferKind? Kind, DebtHintStrength? DebtStrength) Parse(ExtractionResult? extraction, TransactionType? type)
    {
        var hint = extraction?.RelationHint?.ToLowerInvariant() switch
        {
            "refund" when type == TransactionType.Credit => NotificationRelationHint.Refund,
            "reversal" when type == TransactionType.Credit => NotificationRelationHint.Reversal,
            "transfer" when type is TransactionType.Debit or TransactionType.Credit => NotificationRelationHint.Transfer,
            "debt_repayment" when type is TransactionType.Debit or TransactionType.Credit => NotificationRelationHint.DebtRepayment,
            _ => (NotificationRelationHint?)null,
        };
        var kind = hint == NotificationRelationHint.Transfer ? TransferKindColumn.Parse(extraction?.TransferKind) : null;
        // Thiếu độ mạnh (ai-service cũ hơn) thì coi là yếu: chỉ gợi ý khi tên người khớp, an toàn hơn đoán là mạnh.
        DebtHintStrength? strength = hint != NotificationRelationHint.DebtRepayment ? null
            : extraction?.DebtHintStrength?.ToLowerInvariant() == "strong" ? DebtHintStrength.Strong : DebtHintStrength.Weak;
        return (hint, kind, strength);
    }
}
