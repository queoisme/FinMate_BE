namespace FinMate.Application.FinancialAccounts.Commands;

public record ToggleAccountMonitoringCommand(Guid UserId, Guid AccountId, bool IsMonitored);
