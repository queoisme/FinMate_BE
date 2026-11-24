using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Monitoring;

public record OpsErrorEventDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Service,
    string Source,
    string? ExceptionType,
    string? SourceContext,
    string? MessageTemplate,
    string? RequestPath,
    string? TraceId,
    string Fingerprint,
    string? ShortType = null,
    string? Title = null,
    string? Explanation = null,
    string? Action = null);

public record OpsErrorEventPageDto(IReadOnlyList<OpsErrorEventDto> Items, int Total, int Offset, int Limit);

public sealed record ErrorEventQuery(string? Service, string? ExceptionType, DateTimeOffset? From, DateTimeOffset? To, int Offset, int Limit);

public interface IAdminErrorEventService
{
    Task<OpsErrorEventPageDto> ListAsync(ErrorEventQuery query, CancellationToken ct = default);
}

public sealed class AdminErrorEventService : IAdminErrorEventService
{
    private const int MaxPageSize = 200;

    private readonly IMonitoringRepository _repository;
    private readonly ISystemConfigProvider _config;

    public AdminErrorEventService(IMonitoringRepository repository, ISystemConfigProvider config)
    {
        _repository = repository;
        _config = config;
    }

    public async Task<OpsErrorEventPageDto> ListAsync(ErrorEventQuery query, CancellationToken ct = default)
    {
        var offset = Math.Max(query.Offset, 0);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListErrorEventsAsync(query.Service, query.ExceptionType, query.From, query.To, offset, limit, ct);
        var hints = (await _config.GetAsync(ct)).Document.Monitoring.ErrorHints;
        return new OpsErrorEventPageDto(
            items.Select(e =>
            {
                var hint = FindHint(hints, e.ExceptionType);
                return new OpsErrorEventDto(e.Id, e.CreatedAt, e.Service, e.Source.ToString(), e.ExceptionType, e.SourceContext, e.MessageTemplate, e.RequestPath, e.TraceId, e.Fingerprint,
                    ShortType(e.ExceptionType), hint?.Title, hint?.Explanation, string.IsNullOrWhiteSpace(hint?.Action) ? null : hint!.Action);
            }).ToList(),
            total,
            offset,
            limit);
    }

    /// <summary>Mục chú giải đầu tiên có <c>Match</c> nằm trong tên kiểu lỗi; null khi chưa có chú giải.</summary>
    public static ErrorHint? FindHint(IReadOnlyList<ErrorHint> hints, string? exceptionType)
        => string.IsNullOrEmpty(exceptionType)
            ? null
            : hints.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.Match) && exceptionType.Contains(h.Match, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tên kiểu lỗi bỏ phần namespace, để đọc được trong bảng.</summary>
    public static string? ShortType(string? exceptionType)
        => string.IsNullOrEmpty(exceptionType) ? null : exceptionType[(exceptionType.LastIndexOf('.') + 1)..];
}
