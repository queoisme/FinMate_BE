using FinMate.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Như <see cref="AuthApiFactory"/> nhưng ghi lại mọi push, để test hoàn tiền kiểm được action và việc KHÔNG có cảnh báo mới.</summary>
public sealed class RefundApiFactory : AuthApiFactory
{
    public RecordingPushService Push { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPushNotificationService>();
            services.AddSingleton<IPushNotificationService>(Push);
        });
    }
}
