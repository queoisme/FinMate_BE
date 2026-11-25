namespace FinMate.Application.Common.Exceptions;

/// <summary>410: thứ được hỏi đã từng có nhưng không còn dùng được nữa (vd. link tải đã dùng, file đã hết hạn).</summary>
public class GoneException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 410;

    public GoneException(string code, string message) : base(message)
    {
        ErrorCode = code;
    }
}
