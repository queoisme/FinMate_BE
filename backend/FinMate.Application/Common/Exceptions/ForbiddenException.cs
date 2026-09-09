namespace FinMate.Application.Common.Exceptions;

public class ForbiddenException : FinMateException
{
    public override string ErrorCode => "FORBIDDEN";
    public override int HttpStatusCode => 403;

    public ForbiddenException(string message) : base(message)
    {
    }
}
