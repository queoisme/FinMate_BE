namespace FinMate.Application.Common.Exceptions;

public class ConflictException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 409;

    public ConflictException(string code, string message) : base(message)
        => ErrorCode = code;
}
