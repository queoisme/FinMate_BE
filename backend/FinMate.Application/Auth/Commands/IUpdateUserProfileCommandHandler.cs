using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IUpdateUserProfileCommandHandler
{
    Task<UserProfileDto> HandleAsync(UpdateUserProfileCommand command, CancellationToken ct = default);
}
