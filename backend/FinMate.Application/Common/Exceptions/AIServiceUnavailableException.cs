namespace FinMate.Application.Common.Exceptions;

public class AIServiceUnavailableException : FinMateException
{
    public override string ErrorCode => NotificationErrorCodes.AiServiceUnavailable;
    public override int HttpStatusCode => 503;

    public AIServiceUnavailableException(string message) : base(message)
    {
    }
}
