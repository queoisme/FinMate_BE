using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Support;

public interface ISupportRetentionService
{
    /// <summary>Đóng các ca đã giải quyết lâu mà khách không phản hồi, rồi xóa ảnh đã hết hạn. Trả về (số ca đóng, số ảnh xóa).</summary>
    Task<(int Closed, int AttachmentsDeleted)> RunAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class SupportRetentionService : ISupportRetentionService
{
    private const int BatchSize = 200;

    private readonly ISupportRepository _support;
    private readonly ISystemConfigProvider _config;

    public SupportRetentionService(ISupportRepository support, ISystemConfigProvider config)
    {
        _support = support;
        _config = config;
    }

    public async Task<(int Closed, int AttachmentsDeleted)> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Support;
        var stale = await _support.ListResolvedBeforeAsync(now.AddDays(-settings.AutoCloseResolvedDays), BatchSize, ct);
        foreach (var ticket in stale)
        {
            await SupportTicketClosing.CloseAsync(_support, ticket, settings, now, ct);
        }

        var deleted = await _support.DeleteExpiredAttachmentsAsync(now, ct);
        return (stale.Count, deleted);
    }
}
