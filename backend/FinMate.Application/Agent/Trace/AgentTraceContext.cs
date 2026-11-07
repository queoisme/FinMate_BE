namespace FinMate.Application.Agent;

/// <summary>
/// Vết của MỘT lượt hỏi (đăng ký theo phạm vi request). Bộ điều phối gọi <see cref="Begin"/> ở đầu lượt; <c>AgentAiClient</c> đọc <see cref="TraceId"/> và <see cref="UserId"/> để gửi
/// <c>X-Trace-Id</c> và <c>X-User-Hash</c> (mã băm SHA-256, không phải id thật) sang ai-service, nơi mỗi bước ghi một dòng <c>agent_runs</c>. Không chứa nội dung câu hỏi hay số tiền.
/// </summary>
public sealed class AgentTraceContext
{
    public Guid TraceId { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsActive => TraceId != Guid.Empty;

    public void Begin(Guid traceId, Guid userId)
    {
        TraceId = traceId;
        UserId = userId;
    }
}
