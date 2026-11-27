using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Application.DataExport.Package;
using FinMate.Application.Monitoring;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Phase 91: các nhánh lỗi của job dựng gói (BR-EXP-09, D14, D15) — quá cỡ, lỗi tạm thời, hết lượt, cờ tắt, job quét.</summary>
public class PersonalDataExportRunnerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<IPersonalDataExportRepository> _exports = new();
    private readonly Mock<IPersonalDataReader> _reader = new();
    private readonly Mock<IIncidentService> _incidents = new();
    private readonly Mock<IPushNotificationService> _push = new();
    private readonly Mock<IPersonalDataExportQueue> _queue = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private SystemConfigDocument _document = new();

    private PersonalDataExportRunner Runner()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = UserId, Email = "a@finmate.local", DisplayName = "A" });
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new SystemConfigSnapshot(1, _document, null));
        var agent = new Mock<IAgentAiClient>();
        agent.Setup(a => a.GetMemoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var transactions = new Mock<IDataExportRepository>();
        transactions.Setup(t => t.StreamTransactionsAsync(It.IsAny<Guid>(), It.IsAny<TransactionExportFilter>(), It.IsAny<CancellationToken>()))
            .Returns(NoRows());
        var builder = new PersonalDataArchiveBuilder(_reader.Object, transactions.Object, agent.Object, NullLogger<PersonalDataArchiveBuilder>.Instance);
        var protector = new Mock<IExportArchiveProtector>();
        protector.Setup(p => p.Protect(It.IsAny<Guid>(), It.IsAny<byte[]>())).Returns(([1], [2]));
        var notifier = new PersonalDataExportNotifier(_push.Object, Mock.Of<IEmailSender>(), users.Object, NullLogger<PersonalDataExportNotifier>.Instance);
        return new PersonalDataExportRunner(_exports.Object, users.Object, builder, protector.Object, config.Object, _audit.Object, _incidents.Object,
            notifier, _queue.Object, new ManualClock(DateTimeOffset.UtcNow), NullLogger<PersonalDataExportRunner>.Instance);
    }

    private static async IAsyncEnumerable<TransactionExportRow> NoRows()
    {
        await Task.CompletedTask;
        yield break;
    }

    private PersonalDataExport Leased(int attempts)
    {
        var export = new PersonalDataExport { Id = Guid.NewGuid(), UserId = UserId, Status = PersonalDataExportStatus.Running, Attempts = attempts };
        _exports.Setup(e => e.TryLeaseAsync(export.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), PersonalDataExportRunner.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(export);
        _exports.Setup(e => e.MarkFailedAsync(export.Id, It.IsAny<string>(), false, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return export;
    }

    [Fact]
    public async Task ATooLargeArchive_Fails_DoesNotCount_OpensANormalIncidentWithoutTheUser_AndTellsTheUser()
    {
        var export = Leased(attempts: 1);
        _document = new SystemConfigDocument { DataExport = new DataExportSettings { MaxArchiveMb = 1 } };
        _reader.Setup(r => r.ReadSectionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PackageSection("big.json", Enumerable.Range(0, 60_000).Select(i => Guid.NewGuid().ToString()).ToList(), true)]);

        await Runner().RunAsync(export.Id);

        _exports.Verify(e => e.MarkFailedAsync(export.Id, DataExportFailureCodes.ArchiveTooLarge, false, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.RaiseAsync(It.Is<IncidentSignal>(s => s.Fingerprint == PersonalDataExportRunner.TooLargeFingerprint
                && s.Severity == IncidentSeverity.Normal && !s.Title.Contains(UserId.ToString()) && !(s.TechnicalDetail ?? "").Contains(UserId.ToString())),
            1, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _push.Verify(p => p.NotifyAsync(UserId, It.IsAny<string>(), It.IsAny<string>(),
            It.Is<IReadOnlyDictionary<string, string>>(d => d["action"] == PersonalDataExportNotifier.FailedAction), It.IsAny<CancellationToken>()), Times.Once);
        _exports.Verify(e => e.MarkReadyAsync(It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ATransientError_HandsTheWorkBack_UntilTheLastAttempt()
    {
        var early = Leased(attempts: 1);
        var last = Leased(attempts: PersonalDataExportRunner.MaxAttempts);
        _reader.Setup(r => r.ReadSectionsAsync(UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        await Runner().RunAsync(early.Id);
        await Runner().RunAsync(last.Id);

        _exports.Verify(e => e.ReleaseAsync(early.Id, It.IsAny<CancellationToken>()), Times.Once);
        _exports.Verify(e => e.MarkFailedAsync(early.Id, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _exports.Verify(e => e.MarkFailedAsync(last.Id, DataExportFailureCodes.InternalError, false, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(AuditEvents.DataExportFailed, UserId, null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithTheSwitchOff_NothingIsLeased_AndTheSweepDoesNothing()
    {
        _document = new SystemConfigDocument { Features = new FeatureSettings { DataExport = false } };

        await Runner().RunAsync(Guid.NewGuid());
        await Runner().SweepAsync();

        _exports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TheSweep_RequeuesLostWork_AndFailsWorkThatUsedUpItsAttempts()
    {
        var lost = new PersonalDataExport { Id = Guid.NewGuid(), UserId = UserId, Status = PersonalDataExportStatus.Pending, Attempts = 0 };
        var dead = new PersonalDataExport { Id = Guid.NewGuid(), UserId = UserId, Status = PersonalDataExportStatus.Running, Attempts = PersonalDataExportRunner.MaxAttempts };
        _exports.Setup(e => e.FindStaleAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([lost, dead]);
        _exports.Setup(e => e.MarkFailedAsync(dead.Id, DataExportFailureCodes.InternalError, false, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Runner().SweepAsync();

        _queue.Verify(q => q.Enqueue(lost.Id), Times.Once);
        _queue.Verify(q => q.Enqueue(dead.Id), Times.Never);
        _exports.Verify(e => e.MarkFailedAsync(dead.Id, DataExportFailureCodes.InternalError, false, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
