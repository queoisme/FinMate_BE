namespace FinMate.Application.Common.Models;

public record NotificationAnalysisResultDto(Guid NotificationLogId, string Status, Guid? DraftTransactionId);
