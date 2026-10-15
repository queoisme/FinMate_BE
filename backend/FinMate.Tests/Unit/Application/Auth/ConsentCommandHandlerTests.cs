using FinMate.Application.Auth.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

/// <summary>Đồng ý điều khoản chung và đồng ý gửi tên món cho AI: ghi phiên bản + thời điểm, chỉ chấp nhận đúng phiên bản hiện hành.</summary>
public class ConsentCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ITrainingSampleService> _training = new();
    private readonly ConsentCommandHandler _handler;
    private readonly User _user = new() { Id = Guid.NewGuid(), Email = "a@finmate.local", DisplayName = "A" };

    public ConsentCommandHandlerTests()
    {
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _training.Setup(t => t.EraseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _handler = new ConsentCommandHandler(_users.Object, _cache.Object, _training.Object);
    }

    [Fact]
    public void NewUser_HasAgreedToNothing()
    {
        _user.TermsAccepted.Should().BeFalse();
        _user.AiConsentGranted.Should().BeFalse();
        _user.TrainingConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptAi_RecordsVersionAndTime_AndLeavesTermsAlone()
    {
        var before = DateTimeOffset.UtcNow;

        var dto = await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Ai, ConsentVersions.Ai));

        dto.AiConsentGranted.Should().BeTrue();
        dto.TermsAccepted.Should().BeFalse();
        _user.AiConsentVersion.Should().Be(ConsentVersions.Ai);
        _user.AiConsentAt.Should().BeOnOrAfter(before);
        _users.Verify(r => r.UpdateAsync(_user, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveAsync(It.Is<string>(k => k.Contains(_user.Id.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptTerms_RecordsVersionAndTime_AndLeavesAiAlone()
    {
        var dto = await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Terms, ConsentVersions.Terms));

        dto.TermsAccepted.Should().BeTrue();
        dto.AiConsentGranted.Should().BeFalse();
        _user.TermsVersion.Should().Be(ConsentVersions.Terms);
        _user.TermsAcceptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AcceptTraining_RecordsVersionAndTime_AndLeavesAiAndTermsAlone()
    {
        var dto = await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Training, ConsentVersions.Training));

        dto.TrainingConsentGranted.Should().BeTrue();
        dto.AiConsentGranted.Should().BeFalse();
        dto.TermsAccepted.Should().BeFalse();
        _user.TrainingConsentVersion.Should().Be(ConsentVersions.Training);
        _user.TrainingConsentAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ConsentKind.Ai)]
    [InlineData(ConsentKind.Terms)]
    [InlineData(ConsentKind.Training)]
    public async Task AcceptingAStaleVersion_IsRefused_AndNothingIsSaved(ConsentKind kind)
    {
        // app cũ hiển thị câu chữ cũ: không được "đồng ý" thay cho câu chữ mới
        var act = () => _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, kind, 0));

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(AuthErrorCodes.ConsentVersionOutdated);
        _users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ConsentGivenToAnOlderVersion_NoLongerCounts()
    {
        _user.AiConsentVersion = ConsentVersions.Ai - 1;
        _user.TermsVersion = ConsentVersions.Terms - 1;
        _user.TrainingConsentVersion = ConsentVersions.Training - 1;

        _user.AiConsentGranted.Should().BeFalse();
        _user.TermsAccepted.Should().BeFalse();
        _user.TrainingConsentGranted.Should().BeFalse();
    }

    [Fact]
    public async Task WithdrawAi_ClearsOnlyTheAiConsent()
    {
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Terms, ConsentVersions.Terms));
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Ai, ConsentVersions.Ai));

        var dto = await _handler.WithdrawAsync(_user.Id, ConsentKind.Ai);

        dto.AiConsentGranted.Should().BeFalse();
        dto.TermsAccepted.Should().BeTrue();
        _user.AiConsentVersion.Should().BeNull();
        _user.AiConsentAt.Should().BeNull();
    }

    [Fact]
    public async Task WithdrawTraining_ClearsOnlyTheTrainingConsent()
    {
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Ai, ConsentVersions.Ai));
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Training, ConsentVersions.Training));

        var dto = await _handler.WithdrawAsync(_user.Id, ConsentKind.Training);

        dto.TrainingConsentGranted.Should().BeFalse();
        dto.AiConsentGranted.Should().BeTrue();
        _user.TrainingConsentVersion.Should().BeNull();
        _user.TrainingConsentAt.Should().BeNull();
    }

    [Fact]
    public async Task WithdrawTraining_ErasesWhatTheUserAlreadyContributed()
    {
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Training, ConsentVersions.Training));

        await _handler.WithdrawAsync(_user.Id, ConsentKind.Training);

        _training.Verify(t => t.EraseAsync(_user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithdrawTraining_WhenTheEraseFails_TellsTheCallerAndStillClearsConsent()
    {
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Training, ConsentVersions.Training));
        _training.Setup(t => t.EraseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.WithdrawAsync(_user.Id, ConsentKind.Training);

        await act.Should().ThrowAsync<AIServiceUnavailableException>();  // không được báo "đã xóa" khi chưa xóa được
        _user.TrainingConsentVersion.Should().BeNull();  // thu thêm thì đã dừng; bấm lại sẽ xóa tiếp
    }

    [Fact]
    public async Task WithdrawingAiOrAcceptingAnythingNeverErasesTrainingSamples()
    {
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Training, ConsentVersions.Training));
        await _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, ConsentKind.Ai, ConsentVersions.Ai));
        await _handler.WithdrawAsync(_user.Id, ConsentKind.Ai);

        _training.Verify(t => t.EraseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUndefinedKindIsRefusedNotTreatedAsTraining()
    {
        var accept = () => _handler.AcceptAsync(new AcceptConsentCommand(_user.Id, (ConsentKind)7, 1));
        var withdraw = () => _handler.WithdrawAsync(_user.Id, (ConsentKind)7);

        (await accept.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(AuthErrorCodes.ConsentKindUnknown);
        (await withdraw.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(AuthErrorCodes.ConsentKindUnknown);
        _training.Verify(t => t.EraseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TermsCannotBeWithdrawn()
    {
        var act = () => _handler.WithdrawAsync(_user.Id, ConsentKind.Terms);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(AuthErrorCodes.ConsentNotWithdrawable);
    }

    [Fact]
    public async Task UnknownUser_IsNotFound()
    {
        var act = () => _handler.WithdrawAsync(Guid.NewGuid(), ConsentKind.Ai);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
