namespace FinMate.Application.Common.Exceptions;

public static class AuthErrorCodes
{
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AccountLocked = "AUTH_ACCOUNT_LOCKED";
    public const string TokenExpired = "AUTH_TOKEN_EXPIRED";
    public const string TokenInvalid = "AUTH_TOKEN_INVALID";
    public const string TokenReuseDetected = "AUTH_TOKEN_REUSE_DETECTED";
    public const string EmailAlreadyExists = "AUTH_EMAIL_ALREADY_EXISTS";
}
