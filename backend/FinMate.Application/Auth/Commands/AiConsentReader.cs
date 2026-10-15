using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Auth.Commands;

public class AiConsentReader(IUserRepository users) : IAiConsentReader
{
    public async Task<bool> HasAiConsentAsync(Guid userId, CancellationToken ct = default)
        => (await users.GetByIdAsync(userId, ct))?.AiConsentGranted ?? false;
}
