using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Auth.Otp;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Phase 91: thứ tự kiểm khi tạo yêu cầu gói (BR-EXP-06, D8): cờ, khoá chủ, hạn mức TRƯỚC mã, rồi mã.</summary>
public class PersonalDataExportServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<IPersonalDataExportRepository> _exports = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<IExportArchiveProtector> _protector = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IPersonalDataExportQueue> _queue = new();
    private SystemConfigDocument _document = new();

    public PersonalDataExportServiceTests()
    {
        _protector.SetupGet(p => p.Available).Returns(true);
        _exports.Setup(e => e.CountedRequestTimesSinceAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _exports.Setup(e => e.TryAddAsync(It.IsAny<PersonalDataExport>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _otp.Setup(o => o.VerifyAsync("a@finmate.local", OtpPurpose.DataExport, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(OtpVerifyResult.Valid);
    }

    private PersonalDataExportService Service()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = UserId, Email = "a@finmate.local", DisplayName = "A" });
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new SystemConfigSnapshot(1, _document, null));
        return new PersonalDataExportService(_exports.Object, users.Object, _otp.Object, _email.Object, _protector.Object, config.Object,
            Mock.Of<IAuditLogService>(), _queue.Object, new ManualClock(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AValidCode_CreatesAPendingRequest()
    {
        var dto = await Service().RequestAsync(UserId, "123456", null);

        dto.Status.Should().Be(FinMate.Domain.Enums.PersonalDataExportStatus.Pending);
        _exports.Verify(e => e.TryAddAsync(It.Is<PersonalDataExport>(x => x.UserId == UserId && x.CountsTowardLimit), It.IsAny<CancellationToken>()), Times.Once);
        _queue.Verify(q => q.Enqueue(dto.Id), Times.Once);
    }

    [Fact]
    public async Task WhenOverTheLimit_TheCodeIsNotChecked_SoItIsNotUsedUp()
    {
        _exports.Setup(e => e.CountedRequestTimesSinceAsync(UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([DateTimeOffset.UtcNow.AddHours(-2)]);

        await FluentActions.Invoking(() => Service().RequestAsync(UserId, "123456", null)).Should().ThrowAsync<TooManyRequestsException>();
        _otp.Verify(o => o.VerifyAsync(It.IsAny<string>(), It.IsAny<OtpPurpose>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TooManyWrongCodes_Is403WithItsOwnCode()
    {
        _otp.Setup(o => o.VerifyAsync("a@finmate.local", OtpPurpose.DataExport, "999999", It.IsAny<CancellationToken>())).ReturnsAsync(OtpVerifyResult.TooManyAttempts);

        (await FluentActions.Invoking(() => Service().RequestAsync(UserId, "999999", null)).Should().ThrowAsync<ForbiddenException>())
            .Which.ErrorCode.Should().Be(DataExportErrorCodes.OtpTooManyAttempts);
    }

    [Fact]
    public async Task WithoutTheMasterKey_TheFullPackageIs422_AndNoCodeIsSent()
    {
        _protector.SetupGet(p => p.Available).Returns(false);

        (await FluentActions.Invoking(() => Service().SendOtpAsync(UserId)).Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(DataExportErrorCodes.Unavailable);
        _otp.Verify(o => o.IssueAsync(It.IsAny<string>(), It.IsAny<OtpPurpose>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheSwitchOff_Is422()
    {
        _document = new SystemConfigDocument { Features = new FeatureSettings { DataExport = false } };

        (await FluentActions.Invoking(() => Service().RequestAsync(UserId, "123456", null)).Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(DataExportErrorCodes.Disabled);
    }

    [Fact]
    public async Task WhenTheEmailCannotBeSent_TheCooldownIsCleared_And422()
    {
        _otp.Setup(o => o.IssueAsync("a@finmate.local", OtpPurpose.DataExport, It.IsAny<CancellationToken>())).ReturnsAsync(new OtpIssueResult(true, "123456"));
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        (await FluentActions.Invoking(() => Service().SendOtpAsync(UserId)).Should().ThrowAsync<BusinessRuleException>())
            .Which.ErrorCode.Should().Be(DataExportErrorCodes.OtpEmailFailed);
        _otp.Verify(o => o.ClearCooldownAsync("a@finmate.local", OtpPurpose.DataExport, It.IsAny<CancellationToken>()), Times.Once);
    }
}
