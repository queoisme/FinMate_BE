namespace FinMate.Application.Common.Models;

/// <param name="RelationHint">Phase 81: <c>Refund</c>/<c>Reversal</c> khi AI đoán nháp là hoàn/đảo; null nếu không.</param>
/// <param name="SuggestedRelatedTransactions">Tối đa 3 khoản chi gốc gợi ý (tốt nhất trước) khi có <paramref name="RelationHint"/>.</param>
public record NotificationAnalysisResultDto(
    Guid NotificationLogId,
    string Status,
    Guid? DraftTransactionId,
    string? RelationHint = null,
    IReadOnlyList<RefundSuggestionDto>? SuggestedRelatedTransactions = null);
