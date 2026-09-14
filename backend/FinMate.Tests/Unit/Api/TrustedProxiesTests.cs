using System.Net;
using FinMate.API.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.HttpOverrides;
using Xunit;

namespace FinMate.Tests.Unit.Api;

/// <summary>
/// Sau reverse proxy, RemoteIpAddress là IP của PROXY — mà rate limiter phân vùng đăng nhập
/// theo IP, nên cả hệ thống sẽ dùng chung một ngăn 10 lần/phút.
/// </summary>
public class TrustedProxiesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotConfiguredMeansNotEnabled(string? configured)
    {
        // Mặc định phải AN TOÀN: tin header khi chưa ai bảo tin là để client tự bịa IP.
        TrustedProxies.BuildOptions(configured).Should().BeNull();
    }

    [Fact]
    public void AListOfProxiesTrustsExactlyThose()
    {
        var options = TrustedProxies.BuildOptions("10.0.0.1, 10.0.0.2");

        options!.KnownProxies.Should().BeEquivalentTo(
            [IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")]);
    }

    [Fact]
    public void TheDefaultLoopbackEntryIsClearedFirst()
    {
        // Không xoá thì danh sách mặc định (chỉ loopback) vẫn còn, và cấu hình đọc như thể
        // đang tin thêm proxy trong khi thực chất vẫn tin cả loopback.
        var options = TrustedProxies.BuildOptions("10.0.0.1");

        options!.KnownNetworks.Should().BeEmpty();
        options.KnownProxies.Should().ContainSingle();
    }

    [Fact]
    public void TrustAllUsesACoveringNetworkNotAnEmptyList()
    {
        // Điểm dễ sai nhất của middleware này: để trống cả hai danh sách KHÔNG có nghĩa "tin
        // tất cả" mà là "không tin ai", và header bị bỏ qua trong im lặng.
        var options = TrustedProxies.BuildOptions("*");

        options!.KnownProxies.Should().BeEmpty();
        options.KnownNetworks.Should().HaveCount(2, "cần phủ cả IPv4 lẫn IPv6");
        options.KnownNetworks.Should().OnlyContain(n => n.PrefixLength == 0);
    }

    [Fact]
    public void BothForwardedForAndProtoAreRead()
    {
        var options = TrustedProxies.BuildOptions("10.0.0.1");

        options!.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedFor);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedProto);
    }

    [Fact]
    public void AMistypedAddressIsSkippedRatherThanCrashingStartup()
    {
        // Một IP gõ sai không đáng để cả dịch vụ không khởi động được, và hệ quả an toàn hơn
        // chứ không nguy hơn: proxy đó đơn giản là không được tin.
        var options = TrustedProxies.BuildOptions("10.0.0.1, khong-phai-ip, 10.0.0.3");

        options!.KnownProxies.Should().HaveCount(2);
    }
}
