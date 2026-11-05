using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.Monitoring;

/// <summary>Ghi lỗi xuống DB theo lô. DB hỏng thì bỏ lô đó và đi tiếp. Log của chính nó được đánh dấu để sink bỏ qua, nếu không DB hỏng sẽ sinh lỗi, lỗi lại vào hàng đợi, thành một vòng lặp.</summary>
public sealed class OpsErrorWriter : BackgroundService
{
    /// <summary>Thuộc tính đánh dấu log do đường ghi lỗi sinh ra, kể cả log của EF trong cùng scope.</summary>
    public const string SuppressProperty = "OpsErrorWriterScope";

    private const int BatchSize = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly Dictionary<string, object> SuppressScope = new() { [SuppressProperty] = true };

    private readonly OpsErrorQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly OpsMonitoringOptions _options;
    private readonly ILogger<OpsErrorWriter> _logger;

    public OpsErrorWriter(OpsErrorQueue queue, IServiceScopeFactory scopes, OpsMonitoringOptions options, ILogger<OpsErrorWriter> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var suppress = _logger.BeginScope(SuppressScope);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
                await Task.Delay(FlushInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Đang tắt: những gì còn trong hàng đợi không quan trọng bằng tắt cho nhanh.
        }
    }

    /// <summary>Không bao giờ ném lỗi, trừ khi bị hủy.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        var batch = new List<OpsErrorEvent>(BatchSize);
        while (batch.Count < BatchSize && _queue.Reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        using var suppress = _logger.BeginScope(SuppressScope);
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            db.OpsErrorEvents.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Không ghi được {Count} lỗi xuống DB: {ExceptionType}", batch.Count, ex.GetType().Name);
        }
    }
}
