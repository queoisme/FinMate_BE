namespace FinMate.Application.Common.Exceptions;

public class AuthenticationException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 401;

    public AuthenticationException(string code, string message) : base(message)
        => ErrorCode = code;
}
