using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Commands;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class VoiceChatCommandHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<ISttServiceClient> _stt = new();
    private readonly Mock<IChatCommandHandler> _chat = new();
    private readonly VoiceChatCommandHandler _handler;
    private readonly ChatResultDto _chatResult = new(ChatActions.Confirm, "add_expense", null, null, null, null);

    public VoiceChatCommandHandlerTests()
    {
        _stt.Setup(s => s.IsConfigured).Returns(true);
        _chat.Setup(c => c.HandleAsync(It.IsAny<ChatCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(_chatResult);
        _handler = new VoiceChatCommandHandler(_stt.Object, _chat.Object);
    }

    private static VoiceChatCommand Command(byte[]? audio = null, string type = "audio/mp4", Guid? account = null)
        => new(UserId, audio ?? [1, 2, 3], "voice.m4a", type, account, null);

    private void Hears(string text) => _stt
        .Setup(s => s.TranscribeAsync(It.IsAny<SttRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SttResponse(text, 2000));

    [Fact]
    public async Task TranscriptGoesThroughTheSameChatFlowAsTypedText()
    {
        Hears("ăn phở bốn mươi nghìn");

        var result = await _handler.HandleAsync(Command());

        result.Transcript.Should().Be("ăn phở bốn mươi nghìn");
        result.Chat.Should().BeSameAs(_chatResult);
        _chat.Verify(c => c.HandleAsync(
            It.Is<ChatCommand>(x => x.UserId == UserId && x.Text == "ăn phở bốn mươi nghìn"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SilenceNeverReachesChat()
    {
        Hears("   ");

        var act = () => _handler.HandleAsync(Command());

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceNoSpeech);
        _chat.Verify(c => c.HandleAsync(It.IsAny<ChatCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("")]
    public async Task NonAudioIsRejectedBeforeAnyCall(string type)
    {
        var act = () => _handler.HandleAsync(Command(type: type));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceAudioInvalid);
        _stt.Verify(s => s.TranscribeAsync(It.IsAny<SttRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyOrOversizedAudioIsRejected()
    {
        var empty = () => _handler.HandleAsync(Command(audio: []));
        var big = () => _handler.HandleAsync(Command(audio: new byte[VoiceChatCommandHandler.MaxAudioBytes + 1]));

        (await empty.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceAudioInvalid);
        (await big.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceAudioInvalid);
    }

    [Fact]
    public async Task M4aTypeIsSentAsMp4()
    {
        Hears("cà phê hai mươi nghìn");

        await _handler.HandleAsync(Command(type: "audio/x-m4a; codecs=aac"));

        _stt.Verify(s => s.TranscribeAsync(It.Is<SttRequest>(r => r.ContentType == "audio/mp4"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutSttServiceItFailsAsUnavailable()
    {
        _stt.Setup(s => s.IsConfigured).Returns(false);

        var act = () => _handler.HandleAsync(Command());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }
}
