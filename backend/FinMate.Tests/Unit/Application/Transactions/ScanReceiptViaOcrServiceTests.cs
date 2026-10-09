using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Quét hóa đơn khi backend điều phối: OCR service đọc và lọc, AI Service phân loại chữ đã lọc.
/// Dữ liệu mẫu là đúng hình dạng phản hồi thật của OCR service (xem OcrServiceClientTests).
/// </summary>
public class ScanReceiptViaOcrServiceTests
{
    private static readonly DateTimeOffset BillDate = new(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7));

    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<IOcrServiceClient> _ocr = new();
    private readonly Mock<IReceiptScanRepository> _scans = new();
    private readonly List<ReceiptScan> _saved = new();
    private readonly ScanReceiptCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public ScanReceiptViaOcrServiceTests()
    {
        _ocr.Setup(o => o.IsConfigured).Returns(true);
        _scans.Setup(r => r.AddAsync(It.IsAny<ReceiptScan>(), It.IsAny<CancellationToken>()))
            .Callback<ReceiptScan, CancellationToken>((scan, _) =>
            {
                scan.Id = Guid.NewGuid();  // EF gán Id khi lưu
                _saved.Add(scan);
            })
            .Returns(Task.CompletedTask);
        _handler = new ScanReceiptCommandHandler(
            _ai.Object, _ocr.Object, _scans.Object, new ReceiptOptions(false), new Mock<IUserRepository>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScanReceiptCommandHandler>.Instance);
        OcrReturns(Highlands());
        AiClassifiesAs(new BillClassifyResponse("food", "dining", 0.8, "brand"));
    }

    private static OcrServiceResponse Highlands() => new(
        true, 16_000, "HIGHLANDS COFFEE", "Highlands Coffee",
        ["HIGHLANDS COFFEE", "Phin Sua Da L", "45.000", "Tong tien:", "16.000"],
        "dining", "brand", false, BillDate, "text");

    private void OcrReturns(OcrServiceResponse response) => _ocr
        .Setup(o => o.AnalyzeAsync(It.IsAny<OcrServiceRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

    private void AiClassifiesAs(BillClassifyResponse response) => _ai
        .Setup(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

    private ScanReceiptCommand Command() => new(_userId, new byte[1024], "hoa-don.jpg", "image/jpeg");

    private void OldOcrRouteWasNotUsed() => _ai.Verify(
        a => a.ScanReceiptAsync(It.IsAny<ScanReceiptRequest>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task AReadableReceiptComesBackWithTheOcrFieldsAndTheAiCategory()
    {
        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("success");
        scanned.AmountCents.Should().Be(16_000);
        scanned.TransactionType.Should().Be("debit");
        scanned.MerchantName.Should().Be("Highlands Coffee");
        scanned.TransactedAt.Should().Be(BillDate);
        scanned.CategorySlug.Should().Be("food");
        scanned.Confidence.Should().Be(0.8);
        OldOcrRouteWasNotUsed();
    }

    [Fact]
    public async Task OnlyTheFilteredTextAndTheMerchantGoToTheAiService()
    {
        await _handler.HandleAsync(Command());

        _ai.Verify(a => a.ClassifyBillAsync(
            It.Is<BillClassifyRequest>(r =>
                r.UserId == _userId && r.MerchantName == "HIGHLANDS COFFEE" && r.Lines.Count == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheOcrServicesOwnCategoryGuessIsNotUsedBecauseTheAiDecides()
    {
        OcrReturns(Highlands() with { Category = "entertainment", CategorySource = "memory" });
        AiClassifiesAs(new BillClassifyResponse("food", "dining", 0.8, "brand"));

        var scanned = await _handler.HandleAsync(Command());

        scanned.CategorySlug.Should().Be("food");  // kết quả của AI, không phải của ocr-service
    }

    [Fact]
    public async Task WhenTheAiDoesNotRecogniseTheShopTheCategoryIsLeftEmptyNotInvented()
    {
        AiClassifiesAs(new BillClassifyResponse(null, null, 0.0, "none"));

        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("success");
        scanned.CategorySlug.Should().BeNull();
        scanned.AmountCents.Should().Be(16_000);
    }

    [Fact]
    public async Task AiServiceDownDoesNotCostTheUserARescan()
    {
        _ai.Setup(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("success");
        scanned.AmountCents.Should().Be(16_000);
        scanned.MerchantName.Should().Be("Highlands Coffee");
        scanned.CategorySlug.Should().BeNull();
    }

    [Fact]
    public async Task OcrServiceDownIsReportedAsUnavailableNotAsAnUnreadableImage()
    {
        _ocr.Setup(o => o.AnalyzeAsync(It.IsAny<OcrServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var act = () => _handler.HandleAsync(Command());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
        _ai.Verify(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreadableImageIsReportedAsUnreadable()
    {
        OcrReturns(new OcrServiceResponse(false, null, null, null, [], null, null, false, null, null));

        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("unreadable");
        scanned.AmountCents.Should().BeNull();
        _ai.Verify(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task TextWithoutATotalIsReportedAsNoAmount(long? total)
    {
        OcrReturns(Highlands() with { TotalAmount = total });

        var scanned = await _handler.HandleAsync(Command());

        scanned.OcrResult.Should().Be("no_amount");
        scanned.AmountCents.Should().BeNull();
        scanned.CategorySlug.Should().BeNull();
    }

    [Fact]
    public async Task ReceiptsTheOcrServiceDoubtsAreNeverReportedAsConfident()
    {
        OcrReturns(Highlands() with { NeedsReview = true });

        var scanned = await _handler.HandleAsync(Command());

        scanned.Confidence.Should().BeLessThanOrEqualTo(ReceiptConfidence.ReviewCeiling);
    }

    [Fact]
    public async Task ImageChecksStillRunBeforeAnythingCrossesTheNetwork()
    {
        var act = () => _handler.HandleAsync(Command() with { Image = new byte[ScanReceiptCommandHandler.MaxImageBytes + 1] });

        await act.Should().ThrowAsync<BusinessRuleException>();
        _ocr.Verify(o => o.AnalyzeAsync(It.IsAny<OcrServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------ bản ghi bóc tách

    [Fact]
    public async Task EveryScanLeavesARecordOfWhatWasExtractedAndItsIdIsReturned()
    {
        var scanned = await _handler.HandleAsync(Command());

        var scan = _saved.Should().ContainSingle().Subject;
        scanned.ScanId.Should().Be(scan.Id);
        scan.UserId.Should().Be(_userId);
        scan.OcrResult.Should().Be("success");
        scan.AmountCents.Should().Be(16_000);
        scan.MerchantName.Should().Be("Highlands Coffee");
        scan.TransactedAt.Should().Be(BillDate);
        scan.CategorySlug.Should().Be("food");
        scan.CategorySource.Should().Be("brand");
        scan.Confidence.Should().Be(scanned.Confidence);
        scan.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void TheRecordHasNoPlaceForTheImageOrTheText()
    {
        // Chỉ các trường bóc tách. Thêm một cột chữ vào đây là một quyết định về dữ liệu cá nhân, phải đi qua duyệt, nên test này chặn
        // việc thêm nó vô tình.
        typeof(ReceiptScan).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            nameof(ReceiptScan.Id), nameof(ReceiptScan.UserId), nameof(ReceiptScan.OcrResult), nameof(ReceiptScan.AmountCents),
            nameof(ReceiptScan.MerchantName), nameof(ReceiptScan.TransactedAt), nameof(ReceiptScan.CategorySlug),
            nameof(ReceiptScan.CategorySource), nameof(ReceiptScan.Confidence), nameof(ReceiptScan.NeedsReview),
            nameof(ReceiptScan.CreatedAt), nameof(ReceiptScan.User));

        // Món cũng vậy: tên + tiền + nhóm, không có chữ OCR gốc của dòng.
        typeof(ReceiptScanItem).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            nameof(ReceiptScanItem.Id), nameof(ReceiptScanItem.ScanId), nameof(ReceiptScanItem.Position), nameof(ReceiptScanItem.Name),
            nameof(ReceiptScanItem.AmountCents), nameof(ReceiptScanItem.CategorySlug), nameof(ReceiptScanItem.CategorySource),
            nameof(ReceiptScanItem.ParentPosition));
    }

    [Fact]
    public async Task UnreadableAndNoAmountScansAreRecordedToo()
    {
        OcrReturns(new OcrServiceResponse(false, null, null, null, [], null, null, false, null, null));
        var unreadable = await _handler.HandleAsync(Command());
        OcrReturns(Highlands() with { TotalAmount = null, NeedsReview = true });
        var noAmount = await _handler.HandleAsync(Command());

        _saved.Select(s => s.OcrResult).Should().Equal("unreadable", "no_amount");
        _saved.All(s => s.AmountCents is null && s.MerchantName is null && s.CategorySlug is null).Should().BeTrue();
        _saved[1].NeedsReview.Should().BeTrue();
        unreadable.ScanId.Should().NotBeNull();
        noAmount.ScanId.Should().NotBeNull();
    }

    [Fact]
    public async Task ARecordKeepsTheAiCategorySourceAndNoneWhenTheAiIsDown()
    {
        _ai.Setup(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        await _handler.HandleAsync(Command());

        _saved.Single().CategorySlug.Should().BeNull();
        _saved.Single().CategorySource.Should().BeNull();
    }

    [Fact]
    public async Task AnAbsurdlyLongMerchantNameIsCutToTheColumnWidth()
    {
        OcrReturns(Highlands() with { MerchantDisplay = new string('A', 500) });

        var scanned = await _handler.HandleAsync(Command());

        _saved.Single().MerchantName.Should().HaveLength(200);
        scanned.MerchantName.Should().HaveLength(500);  // app vẫn nhận tên đầy đủ để người dùng sửa
    }

    [Fact]
    public async Task RejectedImagesLeaveNoRecord()
    {
        var act = () => _handler.HandleAsync(Command() with { Image = [] });

        await act.Should().ThrowAsync<BusinessRuleException>();
        _saved.Should().BeEmpty();
    }
}
