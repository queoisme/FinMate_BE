using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Monitoring;

/// <summary>
/// Dựng bản ghi lỗi chỉ từ những thứ không chứa dữ liệu người dùng: tên kiểu lỗi, vị trí trong code, message template CHƯA điền giá trị, đường dẫn đã bỏ định danh.
/// Cố ý không đọc <c>Exception.Message</c>, stack trace dạng chữ hay giá trị tham số. Message của exception có thể chứa số tiền, token hoặc câu SQL kèm tham số.
/// </summary>
public static partial class OpsErrorFactory
{
    public const string BackendService = "backend";

    private const int MaxTextLength = 300;
    private const int MaxTemplateLength = 500;

    public static OpsErrorEvent Create(
        string service,
        IncidentSource source,
        Exception? exception,
        string? sourceContext,
        string? messageTemplate,
        string? requestPath,
        string? traceId,
        DateTimeOffset at)
    {
        var exceptionType = exception?.GetType().FullName;
        var location = exception is null ? null : Locate(exception);
        var context = Truncate(location ?? sourceContext, MaxTextLength);
        var template = Truncate(messageTemplate, MaxTemplateLength);
        return new OpsErrorEvent
        {
            Id = Guid.NewGuid(),
            CreatedAt = at,
            Service = service,
            Source = source,
            ExceptionType = Truncate(exceptionType, MaxTextLength),
            SourceContext = context,
            MessageTemplate = template,
            RequestPath = Truncate(CleanPath(requestPath), MaxTextLength),
            TraceId = Truncate(traceId, 100),
            Fingerprint = Fingerprint(source, service, exceptionType, context, template),
        };
    }

    /// <summary>Bỏ query string và thay mọi phần có thể là định danh, để đường dẫn vừa an toàn vừa gom được lỗi cùng loại.</summary>
    public static string? CleanPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var query = path.IndexOf('?');
        var clean = query >= 0 ? path[..query] : path;
        clean = GuidPattern().Replace(clean, "{id}");
        return string.Join('/', clean.Split('/').Select(segment => LooksLikeIdentifier(segment) ? "{id}" : segment));
    }

    private static bool LooksLikeIdentifier(string segment)
        => segment.Length >= 16 || (segment.Length > 0 && segment.All(char.IsDigit));

    /// <summary>Hàm đầu tiên trong code của FinMate, để các lỗi đi qua cùng một middleware vẫn phân biệt được với nhau.</summary>
    private static string? Locate(Exception exception)
    {
        var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
        var method = frames.Select(f => f.GetMethod()).FirstOrDefault(m => m?.DeclaringType?.FullName?.StartsWith("FinMate.", StringComparison.Ordinal) == true)
            ?? frames.Select(f => f.GetMethod()).FirstOrDefault(m => m?.DeclaringType is not null);
        if (method?.DeclaringType is not { } type)
        {
            return null;
        }

        // Hàm async được biên dịch thành lớp lồng <Tên>d__N với hàm MoveNext; đổi lại về tên thật.
        var match = StateMachinePattern().Match(type.Name);
        return match.Success && type.DeclaringType is { } outer
            ? $"{outer.FullName}.{match.Groups[1].Value}"
            : $"{type.FullName}.{method.Name}";
    }

    private static string Fingerprint(IncidentSource source, string service, string? exceptionType, string? context, string? template)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{service}|{exceptionType}|{context}|{template}"));
        return $"{source.ToString().ToLowerInvariant()}:{Convert.ToHexStringLower(hash)[..32]}";
    }

    private static string? Truncate(string? value, int length)
        => value is null || value.Length <= length ? value : value[..length];

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex("^<(.+)>d__")]
    private static partial Regex StateMachinePattern();
}
