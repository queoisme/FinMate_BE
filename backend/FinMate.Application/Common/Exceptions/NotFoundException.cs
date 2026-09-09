namespace FinMate.Application.Common.Exceptions;

public class NotFoundException : FinMateException
{
    public override string ErrorCode => "NOT_FOUND";
    public override int HttpStatusCode => 404;

    public NotFoundException(string resourceName, Guid id)
        : base($"{resourceName} với id '{id}' không tồn tại.")
    {
    }
}
