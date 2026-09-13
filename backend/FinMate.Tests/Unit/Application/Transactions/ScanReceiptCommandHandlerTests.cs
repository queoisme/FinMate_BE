using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class ScanReceiptCommandHandlerTests
{
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly ScanReceiptCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();

    public ScanReceiptCommandHandlerTests()
    {
        _handler = new ScanReceiptCommandHandler(_aiServiceClient.Object);

        _aiServiceClient
            .Setup(c => c.ScanReceiptAsync(It.IsAny<ScanReceiptRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScanReceiptResponse(
                "success",
                new ExtractionResult(110_000, "debit", "WINMART+", "WINMART+", null, null, 0.8),
                new CategorizationResult("shopping", 0.8),
                42));
    }

    private ScanReceiptCommand Command(int size = 1024, string contentType = "image/jpeg")
        => new(_userId, new byte[size], "hoa-don.jpg", contentType);

    [Fact]
    public async Task AReadableReceiptComesBackAsPrefillFields()
    {
        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("success");
        scanned.AmountCents.Should().Be(110_000);
        scanned.MerchantName.Should().Be("WINMART+");
        scanned.CategorySlug.Should().Be("shopping");
    }

    [Fact]
    public async Task AnOversizedImageIsRejectedBeforeItCrossesTheNetwork()
    {
        // Chặn ở cả hai đầu. AI Service cũng có giới hạn này, nhưng để một ảnh 50MB đi hết
        // một vòng mạng nội bộ rồi mới bị từ chối là lãng phí có thể tránh được.
        var act = () => _handler.HandleAsync(
            Command(size: ScanReceiptCommandHandler.MaxImageBytes + 1));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(TransactionErrorCodes.ReceiptImageTooLarge);

        _aiServiceClient.Verify(
            c => c.ScanReceiptAsync(It.IsAny<ScanReceiptRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnEmptyImageIsRejected()
    {
        var act = () => _handler.HandleAsync(Command(size: 0));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(TransactionErrorCodes.ReceiptImageInvalid);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("")]
    public async Task OnlyImagesAreAccepted(string contentType)
    {
        var act = () => _handler.HandleAsync(Command(contentType: contentType));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(TransactionErrorCodes.ReceiptImageInvalid);
    }

    [Fact]
    public async Task ContentTypeMatchingIgnoresCase()
    {
        // Client Android gửi "IMAGE/JPEG" là chuyện bình thường; từ chối vì chữ hoa thì lỗi
        // trông giống hệt "ảnh không hợp lệ" mà thật ra ảnh hoàn toàn ổn.
        var scanned = await _handler.HandleAsync(Command(contentType: "IMAGE/JPEG"));

        scanned.AmountCents.Should().Be(110_000);
    }

    [Fact]
    public async Task AnUnavailableAiServiceSurfacesAsItsOwnFailure()
    {
        // Khác hẳn "ảnh không đọc được": một bên bảo người dùng chụp lại, bên kia bảo họ
        // thử lại sau. Nuốt lỗi thành "unreadable" sẽ khiến họ chụp lại mãi không xong.
        _aiServiceClient
            .Setup(c => c.ScanReceiptAsync(It.IsAny<ScanReceiptRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("AI Service không phản hồi."));

        var act = () => _handler.HandleAsync(Command());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task TheRealUserIdIsPassedToInfrastructureWhichHashesIt()
    {
        // Application không băm — đó là việc của Infrastructure (AGENTS.md §3.1), giống hệt
        // AnalyzeRequest.UserId. Băm ở đây sẽ là chỗ băm thứ hai và sớm muộn hai chỗ lệch nhau.
        await _handler.HandleAsync(Command());

        _aiServiceClient.Verify(
            c => c.ScanReceiptAsync(
                It.Is<ScanReceiptRequest>(r => r.UserId == _userId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
