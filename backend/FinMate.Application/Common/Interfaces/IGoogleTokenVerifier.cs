namespace FinMate.Application.Common.Interfaces;

public record GoogleUserInfo(string Sub, string Email, bool EmailVerified, string? Name);

public interface IGoogleTokenVerifier
{
    Task<GoogleUserInfo?> VerifyAsync(string idToken, CancellationToken ct = default);
}
