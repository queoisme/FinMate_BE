namespace FinMate.Application.Common.Exceptions;

/// <summary>Giới hạn tần suất của nghiệp vụ (khác giới hạn của middleware): ví dụ một người chỉ bị nhắc nợ một lần trong 24 giờ.</summary>
public class TooManyRequestsException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode => 429;

    /// <summary>Thời điểm sớm nhất thử lại được.</summary>
    public DateTimeOffset RetryAt { get; }

    public TooManyRequestsException(string code, string message, DateTimeOffset retryAt) : base(message)
    {
        ErrorCode = code;
        RetryAt = retryAt;
    }
}
