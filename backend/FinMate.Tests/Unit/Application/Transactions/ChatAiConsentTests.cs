using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Chat và giọng nói: AI Service chỉ được phép gửi cụm sản phẩm cho AI bên thứ ba khi người dùng ĐANG đồng ý AI. Backend đọc trạng thái đồng ý từ DB
/// mỗi lần (rút đồng ý có hiệu lực ngay) và truyền qua <c>AllowAi</c>; không có bộ đọc thì mặc định tắt.
/// </summary>
public class ChatAiConsentTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly ChatParseResponse Parsed = new("out_of_scope", null, null, null, 5, []);

    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<ISttServiceClient> _stt = new();
    private ChatParseRequest? _sent;

    public ChatAiConsentTests()
    {
        _stt.Setup(s => s.IsConfigured).Returns(true);
        _stt.Setup(s => s.TranscribeAsync(It.IsAny<SttRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SttResponse("ăn phở bốn mươi lăm", 1000));
        _ai.Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatParseRequest, CancellationToken>((r, _) => _sent = r)
            .ReturnsAsync(Parsed);
    }

    private static Mock<IAiConsentReader> Consent(bool granted)
    {
        var consent = new Mock<IAiConsentReader>();
        consent.Setup(c => c.HasAiConsentAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(granted);
        return consent;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TypedChatSendsTheCurrentConsentToTheAiService(bool granted)
    {
        var handler = new ChatAnalyzeCommandHandler(_ai.Object, _stt.Object, new ChatCommandValidator(), Consent(granted).Object);

        await handler.AnalyzeAsync(UserId, "mua ổ cắm điện 60k");

        _sent!.AllowAi.Should().Be(granted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VoiceChatSendsTheCurrentConsentToTheAiService(bool granted)
    {
        var handler = new ChatAnalyzeCommandHandler(_ai.Object, _stt.Object, new ChatCommandValidator(), Consent(granted).Object);

        await handler.AnalyzeVoiceAsync(UserId, [0xFF, 0xFB, 0x90, 0x00, 1, 2, 3, 4], "v.mp3", "audio/mpeg");

        _sent!.AllowAi.Should().Be(granted);
    }

    [Fact]
    public async Task WithoutAConsentReaderNothingIsAllowed()
    {
        var handler = new ChatAnalyzeCommandHandler(_ai.Object, _stt.Object, new ChatCommandValidator());

        await handler.AnalyzeAsync(UserId, "mua ổ cắm điện 60k");

        _sent!.AllowAi.Should().BeFalse();
    }

    [Fact]
    public async Task TheLegacyChatHandlerAlsoSendsTheConsent()
    {
        var handler = new ChatCommandHandler(
            _ai.Object, Mock.Of<ICreateManualTransactionCommandHandler>(), Mock.Of<IFinancialAccountRepository>(), Mock.Of<ICategoryRepository>(),
            Mock.Of<IReportRepository>(), Mock.Of<ITransactionRepository>(), new ChatCommandValidator(), Consent(true).Object);

        await handler.HandleAsync(new ChatCommand(UserId, "mua ổ cắm điện 60k"));

        _sent!.AllowAi.Should().BeTrue();
    }
}
