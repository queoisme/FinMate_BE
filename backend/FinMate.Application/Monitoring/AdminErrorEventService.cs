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
    string Fingerprint);

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

    public AdminErrorEventService(IMonitoringRepository repository)
    {
        _repository = repository;
    }

    public async Task<OpsErrorEventPageDto> ListAsync(ErrorEventQuery query, CancellationToken ct = default)
    {
        var offset = Math.Max(query.Offset, 0);
        var limit = Math.Clamp(query.Limit, 1, MaxPageSize);
        var (items, total) = await _repository.ListErrorEventsAsync(query.Service, query.ExceptionType, query.From, query.To, offset, limit, ct);
        return new OpsErrorEventPageDto(
            items.Select(e => new OpsErrorEventDto(e.Id, e.CreatedAt, e.Service, e.Source.ToString(), e.ExceptionType, e.SourceContext, e.MessageTemplate, e.RequestPath, e.TraceId, e.Fingerprint)).ToList(),
            total,
            offset,
            limit);
    }
}
