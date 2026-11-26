namespace FinMate.Application.Common.Exceptions;

/// <summary>400 kèm mã riêng: thân yêu cầu sai hình dạng (trường lạ, nhóm ngoài phạm vi), khác 422 là đúng hình dạng nhưng sai luật nghiệp vụ.</summary>
public class BadRequestException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 400;

    public BadRequestException(string code, string message) : base(message)
    {
        ErrorCode = code;
    }
}
