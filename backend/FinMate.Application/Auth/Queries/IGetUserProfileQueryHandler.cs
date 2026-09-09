using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Queries;

public interface IGetUserProfileQueryHandler
{
    Task<UserProfileDto> HandleAsync(GetUserProfileQuery query, CancellationToken ct = default);
}
