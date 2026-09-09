namespace FinMate.Application.Auth.Commands;

public record UpdateUserProfileCommand(Guid UserId, string DisplayName);
