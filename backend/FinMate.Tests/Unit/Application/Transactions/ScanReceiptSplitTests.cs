using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Quét tách nhóm theo món: chỉ chạy khi cờ bật, OCR tự thấy các dòng khớp tổng và có từ 2 món thường.
/// Mọi bước hỏng đều rơi về đường cũ (một nhóm) mà lần quét không thất bại.
/// </summary>
public class ScanReceiptSplitTests
{
    private static readonly DateTimeOffset BillDate = new(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7));

    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<IOcrServiceClient> _ocr = new();
    private readonly Mock<IReceiptScanRepository> _scans = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly List<ReceiptScan> _saved = new();
    private readonly List<(Guid ScanId, IReadOnlyList<ReceiptScanItem> Items)> _savedItems = new();
    private readonly List<ClassifyItemsRequest> _classifyRequests = new();
    private readonly Guid _userId = Guid.NewGuid();
    private bool _consent;

    public ScanReceiptSplitTests()
    {
        _ocr.Setup(o => o.IsConfigured).Returns(true);
        _scans.Setup(r => r.AddAsync(It.IsAny<ReceiptScan>(), It.IsAny<CancellationToken>()))
            .Callback<ReceiptScan, CancellationToken>((scan, _) =>
            {
                scan.Id = Guid.NewGuid();
                _saved.Add(scan);
            })
            .Returns(Task.CompletedTask);
        _scans.Setup(r => r.AddItemsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReceiptScanItem>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<ReceiptScanItem>, CancellationToken>((id, items, _) => _savedItems.Add((id, items)))
            .Returns(Task.CompletedTask);
        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new User { Id = _userId, ShareReceiptItemsWithAi = _consent });
        _ai.Setup(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillClassifyResponse("food", "dining", 0.8, "brand"));
        OcrReturns(MixedBill());
        ClassifyItemsReturns(r => Classified(r, "food", "food", "shopping"));
    }

    private ScanReceiptCommandHandler Handler(bool enabled = true) => new(
        _ai.Object, _ocr.Object, _scans.Object, new ReceiptOptions(enabled), _users.Object,
        NullLogger<ScanReceiptCommandHandler>.Instance);

    private ScanReceiptCommand Command() => new(_userId, new byte[1024], "hoa-don.jpg", "image/jpeg");

    // Giảm giá chung và VAT nằm GIỮA các món thường, để chỉ số gửi sang AI phải đánh lại.
    //   0 Cà phê sữa 30.000 | 1 Trân châu 5.000 (topping của 0) | 2 Giảm giá -5.000 | 3 VAT 8% 2.000 | 4 Dầu gội 50.000
    private static OcrServiceResponse MixedBill() => new(
        true, 82_000, "CIRCLE K", "Circle K", ["CIRCLE K", "Tong tien: 82.000"],
        "shopping", "brand", false, BillDate, "text",
        [
            new OcrItem("Cà phê sữa", 30_000, null, false),
            new OcrItem("Trân châu", 5_000, 0, false),
            new OcrItem("Giảm giá", -5_000, null, true),
            new OcrItem("VAT 8%", 2_000, null, false),
            new OcrItem("Dầu gội", 50_000, null, false),
        ],
        ItemsOk: true);

    private void OcrReturns(OcrServiceResponse response) => _ocr
        .Setup(o => o.AnalyzeAsync(It.IsAny<OcrServiceRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

    private void ClassifyItemsReturns(Func<ClassifyItemsRequest, ClassifyItemsResponse> respond) => _ai
        .Setup(a => a.ClassifyItemsAsync(It.IsAny<ClassifyItemsRequest>(), It.IsAny<CancellationToken>()))
        .Callback<ClassifyItemsRequest, CancellationToken>((r, _) => _classifyRequests.Add(r))
        .ReturnsAsync((ClassifyItemsRequest r, CancellationToken _) => respond(r));

    private void ClassifyItemsThrows(Exception ex) => _ai
        .Setup(a => a.ClassifyItemsAsync(It.IsAny<ClassifyItemsRequest>(), It.IsAny<CancellationToken>()))
        .Callback<ClassifyItemsRequest, CancellationToken>((r, _) => _classifyRequests.Add(r))
        .ThrowsAsync(ex);

    private static ClassifyItemsResponse Classified(ClassifyItemsRequest request, params string?[] slugs)
        => new(slugs.Select((s, i) => new ClassifiedItem(i, s, s is null ? "unknown" : "rule")).ToList(), false);

    // Đường cũ: một nhóm, danh mục do /bill/classify quyết.
    private static readonly ScannedReceiptDto OldPathOfMixedBill = new(
        "success", 82_000, "debit", "Circle K", BillDate, "food", 0.8);

    private void ShouldBeOldPath(ScannedReceiptDto dto, ScannedReceiptDto expected)
    {
        dto.Should().BeEquivalentTo(expected, o => o.Excluding(d => d.ScanId));
        dto.Groups.Should().BeNull();
        dto.ScanId.Should().Be(_saved.Single().Id);
        _saved.Single().CategorySource.Should().Be("brand");
        _savedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_FlagKeepsOldBehaviour_AndNeverCallsClassifyItems()
    {
        var dto = await Handler(enabled: false).HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
        _ai.Verify(a => a.ClassifyItemsAsync(It.IsAny<ClassifyItemsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ItemsOkWithTwoGroups_ReturnsGroupsAndStoresItems()
    {
        var dto = await Handler().HandleAsync(Command());

        // Giảm chung -5.000 chia theo tiền gốc (35.000 / 50.000), phần dư làm tròn và VAT vào nhóm lớn nhất.
        dto.Groups.Should().NotBeNull();
        dto.Groups!.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("shopping", 49_058L), ("food", 32_942L));
        dto.Groups!.Sum(g => g.AmountCents).Should().Be(82_000);
        dto.Groups![1].Items.Select(i => i.Name).Should().Equal("Cà phê sữa", "Trân châu");
        dto.ScanId.Should().Be(_saved.Single().Id);
        _saved.Single().NeedsReview.Should().BeFalse();
        _saved.Single().CategorySlug.Should().Be("shopping");
        _ai.Verify(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var (scanId, items) = _savedItems.Single();
        scanId.Should().Be(_saved.Single().Id);
        items.Select(i => (i.Position, i.Name, i.AmountCents, i.CategorySlug, i.CategorySource, i.ParentPosition)).Should().Equal(
            (0, "Cà phê sữa", 30_000L, "food", "rule", (int?)null),
            (1, "Trân châu", 5_000L, "food", "rule", 0),
            (2, "Giảm giá", -5_000L, (string?)null, "discount", null),
            (3, "VAT 8%", 2_000L, null, "surcharge", null),
            (4, "Dầu gội", 50_000L, "shopping", "rule", null));
    }

    [Fact]
    public async Task ItemsNotOk_FallsBackToOldPath()
    {
        OcrReturns(MixedBill() with { ItemsOk = false });

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
        _classifyRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task NullItems_FallsBackToOldPath()
    {
        OcrReturns(MixedBill() with { Items = null });

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
        _classifyRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SingleRegularItem_FallsBackToOldPath()
    {
        // Một món thường + giảm giá + VAT: chưa đủ 2 món thường.
        OcrReturns(MixedBill() with
        {
            TotalAmount = 47_000,
            Items =
            [
                new OcrItem("Dầu gội", 50_000, null, false),
                new OcrItem("Giảm giá", -5_000, null, true),
                new OcrItem("VAT 8%", 2_000, null, false),
            ],
        });

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill with { AmountCents = 47_000 });
        _classifyRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SplitFailsTotalMismatch_FallsBackToOldPath()
    {
        OcrReturns(MixedBill() with { TotalAmount = 90_000 });

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill with { AmountCents = 90_000 });
        _classifyRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task OneCategoryOnly_FallsBackToOldPath()
    {
        ClassifyItemsReturns(r => Classified(r, "food", "food", "food"));

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
    }

    [Fact]
    public async Task UserNotConsenting_SendsAllowAiFalse()
    {
        _consent = false;

        await Handler().HandleAsync(Command());

        _classifyRequests.Single().AllowAi.Should().BeFalse();
    }

    [Fact]
    public async Task UserConsenting_SendsAllowAiTrue()
    {
        _consent = true;

        await Handler().HandleAsync(Command());

        _classifyRequests.Single().AllowAi.Should().BeTrue();
    }

    [Fact]
    public async Task MissingUser_SendsAllowAiFalse()
    {
        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Handler().HandleAsync(Command());

        _classifyRequests.Single().AllowAi.Should().BeFalse();
    }

    [Fact]
    public async Task ConsentTurnedOffIsRespectedImmediately()
    {
        var handler = Handler();
        _consent = true;
        await handler.HandleAsync(Command());

        _consent = false;
        await handler.HandleAsync(Command());

        _classifyRequests.Select(r => r.AllowAi).Should().Equal(true, false);
        _users.Verify(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("nre")]
    [InlineData("unavailable")]
    public async Task ClassifyItemsThrows_FallsBackToOldPathWithoutFailingTheScan(string kind)
    {
        ClassifyItemsThrows(kind == "nre" ? new NullReferenceException() : new AIServiceUnavailableException("down"));

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
    }

    [Fact]
    public async Task ClassifyItemsNullReply_FallsBackToOldPath()
    {
        ClassifyItemsReturns(_ => null!);

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("duplicate")]
    [InlineData("out-of-range")]
    [InlineData("null-items")]
    public async Task ClassifyItemsMalformedReply_FallsBackToOldPath(string kind)
    {
        ClassifyItemsReturns(_ => kind switch
        {
            "short" => new ClassifyItemsResponse([new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(1, "shopping", "rule")], false),
            "duplicate" => new ClassifyItemsResponse(
                [new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(2, "shopping", "rule")], false),
            "out-of-range" => new ClassifyItemsResponse(
                [new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(1, "food", "rule"), new ClassifiedItem(3, "shopping", "rule")], false),
            _ => new ClassifyItemsResponse(null!, false),
        });

        var dto = await Handler().HandleAsync(Command());

        ShouldBeOldPath(dto, OldPathOfMixedBill);
    }

    [Fact]
    public async Task ReplyOutOfOrderIsPlacedByIndex()
    {
        ClassifyItemsReturns(_ => new ClassifyItemsResponse(
            [new ClassifiedItem(2, "shopping", "rule"), new ClassifiedItem(0, "food", "rule"), new ClassifiedItem(1, "food", "rule")], false));

        var dto = await Handler().HandleAsync(Command());

        dto.Groups!.Select(g => g.CategorySlug).Should().Equal("shopping", "food");
    }

    [Fact]
    public async Task UnknownItems_ProduceNullSlugGroupAndNeedsReview()
    {
        ClassifyItemsReturns(r => Classified(r, null, null, "shopping"));

        var dto = await Handler().HandleAsync(Command());

        dto.Groups!.Select(g => g.CategorySlug).Should().Equal("shopping", null);
        _saved.Single().NeedsReview.Should().BeTrue();
        _savedItems.Single().Items[0].CategorySlug.Should().BeNull();
        _savedItems.Single().Items[0].CategorySource.Should().Be("unknown");
    }

    [Fact]
    public async Task UnknownGroupLargest_TopLevelCategoryIsNull()
    {
        ClassifyItemsReturns(r => Classified(r, "food", "food", null));

        var dto = await Handler().HandleAsync(Command());

        dto.Groups![0].CategorySlug.Should().BeNull();
        dto.CategorySlug.Should().BeNull();
        _saved.Single().CategorySlug.Should().BeNull();
        _saved.Single().NeedsReview.Should().BeTrue();
    }

    [Fact]
    public async Task OcrNeedsReviewStillMarksTheScan()
    {
        OcrReturns(MixedBill() with { NeedsReview = true });

        await Handler().HandleAsync(Command());

        _saved.Single().NeedsReview.Should().BeTrue();
    }

    [Fact]
    public async Task OldTopLevelFieldsStillMeanTotalAndLargestGroup()
    {
        var dto = await Handler().HandleAsync(Command());

        dto.OcrResult.Should().Be("success");
        dto.AmountCents.Should().Be(82_000);
        dto.TransactionType.Should().Be("debit");
        dto.MerchantName.Should().Be("Circle K");
        dto.TransactedAt.Should().Be(BillDate);
        dto.CategorySlug.Should().Be("shopping");
        dto.Confidence.Should().Be(0.8);
        _saved.Single().AmountCents.Should().Be(82_000);
    }

    [Fact]
    public async Task RequestToAiCarriesNamesOnly()
    {
        // Topping trỏ món chính nằm SAU giảm giá: parent phải đánh lại theo chỉ số món thường.
        OcrReturns(MixedBill() with
        {
            TotalAmount = 87_000,
            Items =
            [
                new OcrItem("Giảm giá", -5_000, null, true),
                new OcrItem("Phí phục vụ", 2_000, null, false),
                new OcrItem("Trà sữa", 30_000, null, false),
                new OcrItem("Thạch", 5_000, 2, false),
                new OcrItem("Dầu gội", 50_000, null, false),
                new OcrItem("Kem", 5_000, 1, false),  // parent là phụ phí, không phải món thường -> null
            ],
        });

        await Handler().HandleAsync(Command());

        var request = _classifyRequests.Single();
        request.UserId.Should().Be(_userId);
        request.MerchantName.Should().Be("Circle K");
        request.Items.Should().Equal(
            new ClassifyItemInput("Trà sữa", null),
            new ClassifyItemInput("Thạch", 0),
            new ClassifyItemInput("Dầu gội", null),
            new ClassifyItemInput("Kem", null));
    }

    [Fact]
    public async Task AddItemsFailure_DoesNotFailTheScan()
    {
        _scans.Setup(r => r.AddItemsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReceiptScanItem>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var dto = await Handler().HandleAsync(Command());

        dto.Groups.Should().HaveCount(2);
        dto.ScanId.Should().Be(_saved.Single().Id);
    }

    [Fact]
    public async Task LongNamesAreTruncatedWhenStored()
    {
        var longName = new string('a', 300);
        OcrReturns(MixedBill() with
        {
            TotalAmount = 80_000,
            Items = [new OcrItem(longName, 30_000, null, false), new OcrItem("Dầu gội", 50_000, null, false)],
        });
        ClassifyItemsReturns(r => Classified(r, "food", "shopping"));

        await Handler().HandleAsync(Command());

        _savedItems.Single().Items[0].Name.Should().HaveLength(120);
    }
}
