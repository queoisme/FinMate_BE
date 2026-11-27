namespace FinMate.Application.Common.Exceptions;

public class ForbiddenException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 403;

    public ForbiddenException(string message) : this("FORBIDDEN", message)
    {
    }

    /// <summary>403 kèm mã riêng, khi app cần phân biệt lý do (vd. <c>EXPORT_OTP_INVALID</c>).</summary>
    public ForbiddenException(string code, string message) : base(message)
    {
        ErrorCode = code;
    }
}
