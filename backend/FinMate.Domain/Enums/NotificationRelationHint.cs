namespace FinMate.Domain.Enums;

/// <summary>
/// Gợi ý của AI về quan hệ của một thông báo (<c>ai_results.relation_hint</c>). Tách khỏi <see cref="TransactionRelation"/> (Phase 83, D4):
/// <c>transactions.relation</c> chỉ được là hoàn/đảo, còn chuyển ví không bao giờ là một quan hệ của giao dịch mà là đổi loại thành Transfer.
/// </summary>
public enum NotificationRelationHint
{
    Refund,
    Reversal,

    /// <summary>Tiền chuyển giữa các ví của chính người dùng (Phase 83); loại cụ thể ở <c>ai_results.transfer_kind</c>.</summary>
    Transfer,
}

public static class NotificationRelationHintExtensions
{
    /// <summary>Phần mà luồng hoàn/đảo của Phase 81 hiểu được; gợi ý chuyển ví thì null.</summary>
    public static TransactionRelation? AsRefundRelation(this NotificationRelationHint? hint) => hint switch
    {
        NotificationRelationHint.Refund => TransactionRelation.Refund,
        NotificationRelationHint.Reversal => TransactionRelation.Reversal,
        _ => null,
    };
}
