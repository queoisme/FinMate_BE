using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.ListenerHealth;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.ListenerHealth;

/// <summary>BR-LH-04, D8: mất quyền là <c>permission_lost</c>; quá 72 giờ không báo là <c>unknown</c> (thắng cả lần báo mất quyền cũ); mất kết nối mà còn quyền vẫn là <c>ok</c>.</summary>
public class DeviceHealthEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 2, 0, 0, TimeSpan.Zero);
    private static readonly ListenerHealthSettings Settings = new();

    private static DeviceListenerStatus Status(bool permission, bool connected, double hoursAgo)
        => new() { PermissionGranted = permission, ListenerConnected = connected, LastReportedAt = Now.AddHours(-hoursAgo) };

    [Fact]
    public void PermissionAndConnection_IsOk()
        => DeviceHealthEvaluator.Evaluate(Status(true, true, 1), Now, Settings).Should().Be(DeviceHealthState.Ok);

    [Fact]
    public void PermissionDenied_IsPermissionLost()
        => DeviceHealthEvaluator.Evaluate(Status(false, false, 1), Now, Settings).Should().Be(DeviceHealthState.PermissionLost);

    [Fact]
    public void ListenerDisconnectedButPermissionKept_IsOk_ForReminders()
        => DeviceHealthEvaluator.Evaluate(Status(true, false, 1), Now, Settings).Should().Be(DeviceHealthState.Ok);

    [Theory]
    [InlineData(72, DeviceHealthState.Ok)]
    [InlineData(72.1, DeviceHealthState.Unknown)]
    public void NoReportForMoreThanStaleHours_IsUnknown(double hoursAgo, DeviceHealthState expected)
        => DeviceHealthEvaluator.Evaluate(Status(true, true, hoursAgo), Now, Settings).Should().Be(expected);

    [Fact]
    public void AStalePermissionLostReport_IsUnknown_NotPermissionLost()
        => DeviceHealthEvaluator.Evaluate(Status(false, false, 100), Now, Settings).Should().Be(DeviceHealthState.Unknown);
}
