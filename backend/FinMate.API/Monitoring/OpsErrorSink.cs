using FinMate.Infrastructure.Monitoring;
using Serilog.Core;
using Serilog.Events;

namespace FinMate.API.Monitoring;

/// <summary>Nhận log từ mức Error và đưa vào hàng đợi, không đụng tới DB ở đây để không làm chậm request. Chỉ lấy template, không lấy phần đã điền giá trị.</summary>
public sealed class OpsErrorSink : ILogEventSink
{
    private readonly OpsErrorQueue _queue;
    private readonly OpsMonitoringOptions _options;

    public OpsErrorSink(OpsErrorQueue queue, OpsMonitoringOptions options)
    {
        _queue = queue;
        _options = options;
    }

    public void Emit(LogEvent logEvent)
    {
        if (!_options.Enabled
            || logEvent.Level < LogEventLevel.Error
            || logEvent.Properties.ContainsKey(OpsErrorWriter.SuppressProperty))
        {
            return;
        }

        _queue.Enqueue(OpsErrorFactory.Create(
            OpsErrorFactory.BackendService,
            Domain.Enums.IncidentSource.AppError,
            logEvent.Exception,
            Scalar(logEvent, "SourceContext"),
            logEvent.MessageTemplate.Text,
            Scalar(logEvent, "RequestPath"),
            logEvent.TraceId?.ToHexString() ?? Scalar(logEvent, "TraceId"),
            logEvent.Timestamp));
    }

    private static string? Scalar(LogEvent logEvent, string name)
        => logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text } ? text : null;
}
