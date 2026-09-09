namespace FinMate.Application.Common.Exceptions;

public abstract class FinMateException : Exception
{
    public abstract string ErrorCode { get; }
    public abstract int HttpStatusCode { get; }

    protected FinMateException(string message) : base(message)
    {
    }
}
