namespace FinMate.Application.Common.Exceptions;

/// <summary>
/// AI Service từ chối một thay đổi luật vì lý do nghiệp vụ. Giữ nguyên mã HTTP (404 không có, 409 trùng, 422 dữ liệu sai), mã lỗi và thông điệp
/// tiếng Việt do AI Service viết, để giao diện admin hiện đúng lý do.
/// </summary>
public class AiRuleException : FinMateException
{
    public override string ErrorCode { get; }
    public override int HttpStatusCode { get; }

    public AiRuleException(int httpStatusCode, string code, string message) : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ErrorCode = code;
    }
}
