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
        });
    }
}
