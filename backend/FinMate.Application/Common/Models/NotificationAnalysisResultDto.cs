namespace FinMate.Application.Common.Models;

/// <param name="RelationHint">Phase 81: <c>Refund</c>/<c>Reversal</c> khi AI đoán nháp là hoàn/đảo; null nếu không.</param>
/// <param name="SuggestedRelatedTransactions">Tối đa 3 khoản chi gốc gợi ý (tốt nhất trước) khi có <paramref name="RelationHint"/>.</param>
/// <param name="DuplicateOfTransactionId">
/// Phase 82: với <paramref name="Status"/> = <c>Duplicate</c> là giao dịch đã ghi chính sự kiện này (không có nháp mới); với nháp mới là giao
/// dịch mà nháp có thể trùng (nháp đang <c>Suspected</c>). Null nếu không có.
/// </param>
public record NotificationAnalysisResultDto(
    Guid NotificationLogId,
    string Status,
    Guid? DraftTransactionId,
    string? RelationHint = null,
    IReadOnlyList<RefundSuggestionDto>? SuggestedRelatedTransactions = null,
    Guid? DuplicateOfTransactionId = null);
