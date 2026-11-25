using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Đồng hồ thật cộng một độ lệch chỉnh được, để test "sau 72 giờ" mà không chờ.</summary>
public sealed class ShiftableClock : TimeProvider
{
    public TimeSpan Shift { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Shift;
}

/// <summary>Hàng đợi ghi lại: test gọi runner trực tiếp thay vì để Hangfire chạy nền (tất định).</summary>
public sealed class RecordingExportQueue : FinMate.Application.DataExport.IPersonalDataExportQueue
{
    public System.Collections.Concurrent.ConcurrentQueue<Guid> Enqueued { get; } = new();

    public void Enqueue(Guid exportId) => Enqueued.Enqueue(exportId);
}

/// <summary>
/// Như <see cref="AuthApiFactory"/>, cho test xuất dữ liệu (Phase 91): có khoá chủ để mã hoá gói, bắt email để đọc mã OTP, ghi lại mọi push,
/// và đồng hồ chỉnh được.
/// </summary>
public sealed class DataExportApiFactory : AuthApiFactory
{
    public const string MasterKey = "25FD-4k4PLP9WajKvg-hcbqnVhvEcpPi_yswo4AOk4Q=";

    public CapturingEmailSender Email { get; } = new();
    public RecordingPushService Push { get; } = new();
    public ShiftableClock Clock { get; } = new();
    public RecordingExportQueue Queue { get; } = new();

    /// <summary>ai-service không có trong bộ test: bộ nhớ Agent trả từ đây (D2).</summary>
    public Moq.Mock<IAgentAiClient> Agent { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<SecretCipher>();
            services.AddSingleton(new SecretCipher(MasterKey));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.RemoveAll<IPushNotificationService>();
            services.AddSingleton<IPushNotificationService>(Push);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<FinMate.Application.DataExport.IPersonalDataExportQueue>();
            services.AddSingleton<FinMate.Application.DataExport.IPersonalDataExportQueue>(Queue);
            services.RemoveAll<IAgentAiClient>();
            services.AddSingleton(Agent.Object);
        });
    }
}
