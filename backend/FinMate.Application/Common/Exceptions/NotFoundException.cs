namespace FinMate.Application.Common.Exceptions;

public class NotFoundException : FinMateException
{
    public override string ErrorCode => "NOT_FOUND";
    public override int HttpStatusCode => 404;

    public NotFoundException(string resourceName, Guid id)
        : base($"{resourceName} với id '{id}' không tồn tại.")
    {
    }

    /// <summary>Tra theo mã chữ (vd <c>finance_methods.code</c>) thay vì UUID.</summary>
    public NotFoundException(string resourceName, string code)
        : base($"{resourceName} với mã '{code}' không tồn tại.")
    {
    }
}
