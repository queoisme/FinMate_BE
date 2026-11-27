namespace FinMate.Application.Common.Exceptions;

/// <summary>503 kèm mã riêng: việc không hoàn tất vì một bước phía sau tạm hỏng; gửi lại sau là đúng.</summary>
public class ServiceUnavailableException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 503;

    public ServiceUnavailableException(string code, string message) : base(message)
    {
        ErrorCode = code;
    }
}
