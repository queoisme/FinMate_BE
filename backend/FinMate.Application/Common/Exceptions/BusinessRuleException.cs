namespace FinMate.Application.Common.Exceptions;

public class BusinessRuleException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 422;

    public BusinessRuleException(string code, string message) : base(message)
        => ErrorCode = code;
}
