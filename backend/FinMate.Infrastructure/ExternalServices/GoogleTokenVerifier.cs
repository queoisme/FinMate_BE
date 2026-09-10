using FinMate.Application.Common.Interfaces;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.ExternalServices;

public class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly string _clientId;

    public GoogleTokenVerifier(IConfiguration configuration)
    {
        _clientId = configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
    }

    public async Task<GoogleUserInfo?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _clientId },
            });

            return new GoogleUserInfo(payload.Subject, payload.Email, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
