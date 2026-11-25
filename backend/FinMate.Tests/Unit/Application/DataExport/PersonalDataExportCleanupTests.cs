using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Application.Monitoring;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Phase 91 (D15): yêu cầu chưa xong quá 5 ngày mở sự cố Normal (hạn luật 10 ngày); hết thì tự đóng.</summary>
public class PersonalDataExportCleanupTests
{
    private readonly Mock<IPersonalDataExportRepository> _exports = new();
    private readonly Mock<IIncidentService> _incidents = new();
    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);

    private PersonalDataExportCleanup Cleanup()
    {
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemConfigSnapshot(1, new SystemConfigDocument(), null));
        _exports.Setup(e => e.FindDueForExpiryAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new PersonalDataExportCleanup(_exports.Object, config.Object, Mock.Of<IAuditLogService>(), _incidents.Object, _clock,
            NullLogger<PersonalDataExportCleanup>.Instance);
    }

    [Fact]
    public async Task UnfinishedRequestsOlderThanFiveDays_OpenANormalIncident()
    {
        _exports.Setup(e => e.CountUnfinishedSinceBeforeAsync(_clock.Now - TimeSpan.FromDays(5), It.IsAny<CancellationToken>())).ReturnsAsync(2);

        await Cleanup().RunAsync();

        _incidents.Verify(i => i.RaiseAsync(It.Is<IncidentSignal>(s => s.Fingerprint == PersonalDataExportCleanup.OverdueFingerprint && s.Severity == IncidentSeverity.Normal),
            2, _clock.Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NothingOverdue_ClosesTheIncident()
    {
        await Cleanup().RunAsync();

        _incidents.Verify(i => i.ResolveAsync(PersonalDataExportCleanup.OverdueFingerprint, _clock.Now, It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.RaiseAsync(It.IsAny<IncidentSignal>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
