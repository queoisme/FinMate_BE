using FinMate.API.Monitoring;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Monitoring;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class OpsErrorPipelineTests
{
    private readonly OpsErrorQueue _queue = new();

    private OpsErrorSink Sink(bool enabled = true) => new(_queue, new OpsMonitoringOptions(enabled));

    private static LogEvent Log(LogEventLevel level, Exception? exception = null, params LogEventProperty[] properties)
        => new(DateTimeOffset.UtcNow, level, exception, new MessageTemplateParser().Parse("Lỗi {Value}"), properties);

    private static LogEventProperty Prop(string name, object value) => new(name, new ScalarValue(value));

    [Theory]
    [InlineData("/api/v1/transactions/3f2b8c1e-1234-4abc-9def-001122334455/confirm", "/api/v1/transactions/{id}/confirm")]
    [InlineData("/api/v1/items/12345", "/api/v1/items/{id}")]
    [InlineData("/api/v1/thing?token=bi-mat&x=1", "/api/v1/thing")]
    [InlineData("/api/v1/reset/abcdefghijklmnopqrstuvwxyz", "/api/v1/reset/{id}")]
    [InlineData("/api/v1/transactions/scan-receipt", "/api/v1/transactions/scan-receipt")]
    public void PathsLoseIdentifiersAndQueries(string input, string expected)
        => OpsErrorFactory.CleanPath(input).Should().Be(expected);

    [Fact]
    public void TheSameFailureGetsTheSameFingerprint_DifferentValuesDoNotSplitIt()
    {
        static Exception Throw(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var first = OpsErrorFactory.Create("backend", IncidentSource.AppError, Throw("số tiền 100"), "Ctx", "Lỗi {Value}", "/a/11111", null, DateTimeOffset.UtcNow);
        var second = OpsErrorFactory.Create("backend", IncidentSource.AppError, Throw("số tiền 999"), "Ctx", "Lỗi {Value}", "/a/22222", null, DateTimeOffset.UtcNow);
        var other = OpsErrorFactory.Create("backend", IncidentSource.AppError, Throw("x"), "Ctx", "Lỗi khác", "/a", null, DateTimeOffset.UtcNow);

        second.Fingerprint.Should().Be(first.Fingerprint);
        other.Fingerprint.Should().NotBe(first.Fingerprint);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void MonitoringIsOnUnlessExplicitlyOff(string? configured, bool expected)
        => OpsMonitoringOptions.IsEnabled(configured).Should().Be(expected);

    [Fact]
    public void TheSinkTakesErrorsAndAbove()
    {
        var sink = Sink();
        sink.Emit(Log(LogEventLevel.Information));
        sink.Emit(Log(LogEventLevel.Warning));
        _queue.Reader.TryRead(out _).Should().BeFalse();

        sink.Emit(Log(LogEventLevel.Error));
        sink.Emit(Log(LogEventLevel.Fatal));
        _queue.Reader.Count.Should().Be(2);
    }

    [Fact]
    public void TheSinkIgnoresTheWritersOwnLogs_SoAFailingDatabaseCannotLoop()
    {
        Sink().Emit(Log(LogEventLevel.Error, null, Prop(OpsErrorWriter.SuppressProperty, true)));

        _queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void TheSinkDoesNothingWhenMonitoringIsOff()
    {
        Sink(enabled: false).Emit(Log(LogEventLevel.Error));

        _queue.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void AFullQueueNeverBlocksTheCaller()
    {
        var sink = Sink();
        for (var i = 0; i < 5000; i++)
        {
            sink.Emit(Log(LogEventLevel.Error));
        }

        _queue.Reader.Count.Should().Be(2000);
    }

    [Fact]
    public async Task TheWriterSurvivesADatabaseFailure()
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("DB hỏng"));
        var writer = new OpsErrorWriter(_queue, scopes.Object, new OpsMonitoringOptions(true), NullLogger<OpsErrorWriter>.Instance);
        Sink().Emit(Log(LogEventLevel.Error));

        var act = () => writer.FlushAsync();

        await act.Should().NotThrowAsync();
        _queue.Reader.Count.Should().Be(0);
    }

    [Fact]
    public void AJobFailureStoresTheTypeButNotTheMessage()
    {
        var error = OpsErrorFactory.Create("job:ThuJob", IncidentSource.Job, new InvalidOperationException("trừ 123 của a@b.c"), "ThuJob", null, null, null, DateTimeOffset.UtcNow);

        error.ExceptionType.Should().Be("System.InvalidOperationException");
        error.Service.Should().Be("job:ThuJob");
        System.Text.Json.JsonSerializer.Serialize(error).Should().NotContain("a@b.c").And.NotContain("123");
    }

    [Fact]
    public void TheJobFilterRunsBeforeRetriesRewriteTheState()
        => new OpsJobFailureFilter(_queue).Order.Should().BeLessThan(new AutomaticRetryAttribute().Order);
}

public class ProviderFailureTrackerTests
{
    [Fact]
    public void CountsConsecutiveFailuresAndResetsOnSuccess()
    {
        var tracker = new FinMate.Application.Monitoring.ProviderFailureTracker();
        tracker.RecordFailure("brevo", "a");
        tracker.RecordFailure("brevo", "b");

        tracker.Get("brevo").Should().Be(new FinMate.Application.Monitoring.ProviderFailureState(2, "b"));
        tracker.Get("fcm").ConsecutiveFailures.Should().Be(0);

        tracker.RecordSuccess("brevo");
        tracker.Get("brevo").ConsecutiveFailures.Should().Be(0);
    }
}
