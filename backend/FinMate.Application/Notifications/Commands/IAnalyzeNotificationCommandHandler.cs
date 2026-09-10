using FinMate.Application.Common.Models;

namespace FinMate.Application.Notifications.Commands;

public interface IAnalyzeNotificationCommandHandler
{
    Task<NotificationAnalysisResultDto> HandleAsync(AnalyzeNotificationCommand command, CancellationToken ct = default);
}
