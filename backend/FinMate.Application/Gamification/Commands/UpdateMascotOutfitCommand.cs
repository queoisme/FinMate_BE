namespace FinMate.Application.Gamification.Commands;

public record UpdateMascotOutfitCommand(Guid UserId, IReadOnlyList<Guid> ItemIds);
