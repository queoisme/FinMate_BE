using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class ChatAnalyzeTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<ISttServiceClient> _stt = new();
    private readonly ChatAnalyzeCommandHandler _handler;

    private static readonly ChatParseResponse Parsed = new(
        "add_expense", null, null, null, 5,
        [new ChatParseEntry("expense", 25_000, "mua bánh mì", null, "food", null, false, false, null, "rule")],
        TotalAmount: 60_000, TotalMismatch: false, Skipped: ["cà phê"]);

    public ChatAnalyzeTests()
    {
        _stt.Setup(s => s.IsConfigured).Returns(true);
        _ai.Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Parsed);
        _handler = new ChatAnalyzeCommandHandler(_ai.Object, _stt.Object, new ChatCommandValidator());
    }

    [Fact]
    public void ConstructorTakesNoRepositories()
    {
        // Endpoint này không được chạm dữ liệu cá nhân: giao dịch, ví, báo cáo ở lại máy người dùng.
        var parameters = typeof(ChatAnalyzeCommandHandler).GetConstructors().Single().GetParameters();
        parameters.Select(p => p.ParameterType.Name).Should().NotContain(n => n.EndsWith("Repository"));
    }

    [Fact]
    public async Task AnalyzeReturnsTheAiParseAsIs()
    {
        var result = await _handler.AnalyzeAsync(UserId, "mua bánh mì 25k và cà phê");

        result.Should().BeSameAs(Parsed);
        _ai.Verify(a => a.ParseChatAsync(It.Is<ChatParseRequest>(r => r.UserId == UserId && r.Text == "mua bánh mì 25k và cà phê"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeRejectsEmptyText(string text)
    {
        var act = () => _handler.AnalyzeAsync(UserId, text);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        _ai.Verify(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VoiceWithNoSpeechThrowsAndNeverParses()
    {
        _stt.Setup(s => s.TranscribeAsync(It.IsAny<SttRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SttResponse("  ", 1000));

        var act = () => _handler.AnalyzeVoiceAsync(UserId, [1, 2, 3], "v.m4a", "audio/mp4");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceNoSpeech);
        _ai.Verify(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VoiceReturnsTranscriptAndParse()
    {
        _stt.Setup(s => s.TranscribeAsync(It.IsAny<SttRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SttResponse("ăn phở bốn mươi nghìn", 2000));

        var result = await _handler.AnalyzeVoiceAsync(UserId, [1, 2, 3], "v.m4a", "audio/x-m4a");

        result.Transcript.Should().Be("ăn phở bốn mươi nghìn");
        result.Parse.Should().BeSameAs(Parsed);
        _stt.Verify(s => s.TranscribeAsync(It.Is<SttRequest>(r => r.ContentType == "audio/mp4"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VoiceRejectsNonAudio()
    {
        var act = () => _handler.AnalyzeVoiceAsync(UserId, [1], "x.png", "image/png");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(TransactionErrorCodes.VoiceAudioInvalid);
    }
}
