namespace FinMate.Application.Gamification.Queries;

public record GetMissionHistoryQuery(Guid UserId, int Limit);
