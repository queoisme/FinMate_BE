using System.Text.Json;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Tests.Integration.Controllers;

// Swapped in for the real GoogleTokenVerifier in integration tests so we can exercise every
// GoogleLoginCommandHandler branch deterministically without calling Google's servers. Tests
// build the "idToken" as the JSON-serialized GoogleUserInfo they want the fake to return;
// the literal "invalid-token" simulates a signature/expiry failure (real VerifyAsync -> null).
public class FakeGoogleTokenVerifier : IGoogleTokenVerifier
{
    public static string TokenFor(GoogleUserInfo info) => JsonSerializer.Serialize(info);

    public const string InvalidToken = "invalid-token";

    public Task<GoogleUserInfo?> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (idToken == InvalidToken)
        {
            return Task.FromResult<GoogleUserInfo?>(null);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<GoogleUserInfo>(idToken));
        }
        catch (JsonException)
        {
            return Task.FromResult<GoogleUserInfo?>(null);
        }
    }
}
