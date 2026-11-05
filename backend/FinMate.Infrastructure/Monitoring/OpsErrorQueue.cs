using System.Threading.Channels;
using FinMate.Domain.Entities;

namespace FinMate.Infrastructure.Monitoring;

public sealed record OpsMonitoringOptions(bool Enabled)
{
    /// <summary>Bật trừ khi cấu hình ghi rõ là false. Để trống hoặc thiếu biến đều là bật: giám sát tắt thì không ai biết nó đang tắt.</summary>
    public static bool IsEnabled(string? configured)
        => !bool.TryParse(configured, out var enabled) || enabled;
}

/// <summary>Hàng đợi có giới hạn giữa nơi phát sinh lỗi và DB. Đầy thì bỏ bản ghi cũ nhất: giám sát không bao giờ được làm chậm request, nhất là lúc hệ thống đang lỗi hàng loạt.</summary>
public sealed class OpsErrorQueue
{
    private const int Capacity = 2000;

    private readonly Channel<OpsErrorEvent> _channel = Channel.CreateBounded<OpsErrorEvent>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public void Enqueue(OpsErrorEvent error) => _channel.Writer.TryWrite(error);

    public ChannelReader<OpsErrorEvent> Reader => _channel.Reader;
}
