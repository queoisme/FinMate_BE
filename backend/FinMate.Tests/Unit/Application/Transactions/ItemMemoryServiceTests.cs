using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Ghi nhận danh mục người dùng chốt cho từng món của hóa đơn TÁCH: chỉ khi lần quét là tách, bỏ thu nhập/cho vay/không có danh mục,
/// tối đa 60 món mỗi lần gọi, và không bao giờ ném lỗi (phần phụ chạy sau khi giao dịch đã lưu).
/// </summary>
public class ItemMemoryServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _scanId = Guid.NewGuid();
    private readonly Guid _transactionId = Guid.NewGuid();
    private readonly Mock<IReceiptScanRepository> _scans = new();
    private readonly Mock<IAIServiceClient> _ai = new();

    private ItemMemoryService Service() => new(_scans.Object, _ai.Object, NullLogger<ItemMemoryService>.Instance);

    private static Category Cat(string slug, Guid? userId = null) => new() { Id = Guid.NewGuid(), Slug = slug, Name = slug, UserId = userId };

    private static ReceiptScanItem Item(int pos, string name) => new() { Position = pos, Name = name, CategorySlug = "food" };

    private void ScanIs(bool split, params ReceiptScanItem[] items)
    {
        _scans.Setup(r => r.IsSplitAsync(_scanId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(split);
        _scans.Setup(r => r.GetGroupItemsAsync(_scanId, _userId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(items);
    }

    private void VerifySent(Func<LearnItemsRequest, bool> match)
        => _ai.Verify(a => a.LearnItemsAsync(It.Is<LearnItemsRequest>(r => match(r)), It.IsAny<CancellationToken>()), Times.Once);

    private void VerifyNothingSent()
        => _ai.Verify(a => a.LearnItemsAsync(It.IsAny<LearnItemsRequest>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Learn_SendsAcceptedWhenFinalCategorySlugEqualsTheGroup()
    {
        ScanIs(true, Item(0, "Sữa tươi"), Item(1, "Bánh mì"));
        var final = Cat("food", Guid.NewGuid());

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", final);

        VerifySent(r => r.UserId == _userId && r.Items.Count == 2
            && r.Items.All(i => i.CategoryId == final.Id && i.Kind == ItemMemoryKinds.Accepted)
            && r.Items[0].Name == "Sữa tươi" && r.Items[1].Name == "Bánh mì");
    }

    [Fact]
    public async Task Learn_SendsCorrectedWhenItDiffers()
    {
        ScanIs(true, Item(0, "Sữa tươi"));
        var final = Cat("household");

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", final);

        VerifySent(r => r.Items.Count == 1 && r.Items[0].CategoryId == final.Id && r.Items[0].Kind == ItemMemoryKinds.Corrected);
    }

    [Fact]
    public async Task Learn_SendsCorrectedForTheUnknownGroup()
    {
        ScanIs(true, Item(0, "Món lạ"));
        var final = Cat("unknown");  // slug trùng chữ "unknown" cũng vẫn là Corrected: nhóm unknown không phải danh mục AI đề xuất

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, ReceiptScanLink.UnknownGroup, final);

        VerifySent(r => r.Items[0].Kind == ItemMemoryKinds.Corrected);
    }

    [Fact]
    public async Task Learn_SkipsWhenTheScanIsNotSplit()
    {
        ScanIs(false, Item(0, "Sữa tươi"));

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));

        VerifyNothingSent();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("income")]
    [InlineData(Category.LoanSlug)]
    public async Task Learn_SkipsNullIncomeOrLoanFinalCategory(string? slug)
    {
        ScanIs(true, Item(0, "Sữa tươi"));

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", slug is null ? null : Cat(slug));

        VerifyNothingSent();
    }

    [Fact]
    public async Task Learn_SendsNothingWhenThereAreNoEligibleItems()
    {
        ScanIs(true);

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));

        VerifyNothingSent();
    }

    [Fact]
    public async Task Learn_CapsAt60ItemsInPositionOrder()
    {
        // ai-service trả 422 cho > 60 món và lời gọi best-effort nuốt lỗi: phải tự cắt.
        ScanIs(true, Enumerable.Range(0, 75).Select(i => Item(i, $"Mon {i}")).ToArray());

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));

        VerifySent(r => r.Items.Count == 60 && r.Items[0].Name == "Mon 0" && r.Items[59].Name == "Mon 59");
    }

    [Fact]
    public async Task Learn_SwallowsRepositoryAndAiFailures()
    {
        _scans.Setup(r => r.IsSplitAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        var act = () => Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));
        await act.Should().NotThrowAsync();
        VerifyNothingSent();

        ScanIs(true, Item(0, "Sữa tươi"));
        _ai.Setup(a => a.LearnItemsAsync(It.IsAny<LearnItemsRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        act = () => Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LearnFromCorrection_UsesTheLinkedGroup()
    {
        ScanIs(true, Item(0, "Sữa tươi"));
        _scans.Setup(r => r.GetLinkedGroupAsync(_transactionId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((_scanId, "food"));
        var household = Cat("household");

        await Service().LearnFromCorrectionAsync(_userId, _transactionId, household);

        _scans.Verify(r => r.GetGroupItemsAsync(_scanId, _userId, "food", It.IsAny<CancellationToken>()), Times.Once);
        VerifySent(r => r.Items[0].Kind == ItemMemoryKinds.Corrected && r.Items[0].CategoryId == household.Id);
    }

    [Fact]
    public async Task LearnFromCorrection_BackToTheGroupSlug_StillSendsCorrected()
    {
        // Sửa SAU khi lưu luôn là lựa chọn rõ ràng: đổi lại đúng slug AI đề xuất vẫn phải đè dòng "corrected" cũ (accepted không đè được).
        ScanIs(true, Item(0, "Sữa tươi"));
        _scans.Setup(r => r.GetLinkedGroupAsync(_transactionId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((_scanId, "food"));
        var food = Cat("food");

        await Service().LearnFromCorrectionAsync(_userId, _transactionId, food);

        VerifySent(r => r.Items[0].Kind == ItemMemoryKinds.Corrected && r.Items[0].CategoryId == food.Id);
    }

    [Fact]
    public async Task Learn_SkipsBlankNamesBeforeTheCap()
    {
        // ai-service đòi tên dài >= 1: một tên rỗng làm cả lô bị 422 và lỗi bị nuốt. Lọc trước khi cắt 60.
        ScanIs(true, Item(0, ""), Item(1, "   "), Item(2, "Sữa tươi"), Item(3, "Bánh mì"));

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));

        VerifySent(r => r.Items.Count == 2 && r.Items[0].Name == "Sữa tươi" && r.Items[1].Name == "Bánh mì");
    }

    [Fact]
    public async Task Learn_SendsNothingWhenEveryNameIsBlank()
    {
        ScanIs(true, Item(0, ""), Item(1, " "));

        await Service().LearnFromSavedGroupAsync(_userId, _scanId, "food", Cat("household"));

        VerifyNothingSent();
    }

    [Fact]
    public async Task LearnFromCorrection_DoesNothingWhenTheTransactionIsNotLinked()
    {
        _scans.Setup(r => r.GetLinkedGroupAsync(_transactionId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(((Guid, string)?)null);

        await Service().LearnFromCorrectionAsync(_userId, _transactionId, Cat("household"));

        VerifyNothingSent();
    }

    [Fact]
    public async Task Erase_ReturnsTrueOnSuccessAndFalseOnFailure()
    {
        (await Service().EraseAsync(_userId)).Should().BeTrue();
        _ai.Verify(a => a.EraseItemMemoryAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);

        _ai.Setup(a => a.EraseItemMemoryAsync(_userId, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        (await Service().EraseAsync(_userId)).Should().BeFalse();
    }
}
