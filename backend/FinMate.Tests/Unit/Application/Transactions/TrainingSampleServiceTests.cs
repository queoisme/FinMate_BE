using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Gửi chữ hóa đơn ĐÃ lọc và nhãn cuối sang ai-service để học: chỉ khi người dùng đang đồng ý (đúng phiên bản), nhãn chỉ là danh mục hệ thống,
/// và mọi lỗi đều bị nuốt (đây là phần phụ sau khi giao dịch đã lưu).
/// </summary>
public class TrainingSampleServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _scanId = Guid.NewGuid();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAIServiceClient> _ai = new();

    private TrainingSampleService Service() => new(_users.Object, _ai.Object, NullLogger<TrainingSampleService>.Instance);

    private void UserConsentedTo(int? version) => _users
        .Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new User { Id = _userId, TrainingConsentVersion = version });

    private static Category System(string slug) => new() { Id = Guid.NewGuid(), Slug = slug, Name = slug, UserId = null };

    private static readonly string[] Lines = ["Tap hoa Duc Cuong", "Gao thom 5kg 125.000", "Tong cong 125.000"];

    // ---- gửi mẫu chờ nhãn lúc quét

    [Fact]
    public async Task Record_SendsTheFilteredBillWhenTheUserConsented()
    {
        UserConsentedTo(ConsentVersions.Training);

        await Service().RecordAsync(_userId, _scanId, "Tạp hóa Đức Cường", Lines, 125_000, "food", "keyword");

        _ai.Verify(a => a.RecordTrainingSampleAsync(
            It.Is<TrainingSampleRequest>(r => r.ScanId == _scanId && r.UserId == _userId && r.MerchantName == "Tạp hóa Đức Cường"
                && r.Lines.SequenceEqual(Lines) && r.TotalAmount == 125_000 && r.PredictedSlug == "food" && r.PredictedSource == "keyword"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Record_SendsNothingWithoutConsent()
    {
        UserConsentedTo(null);

        await Service().RecordAsync(_userId, _scanId, "X", Lines, 1_000, null, null);

        _ai.Verify(a => a.RecordTrainingSampleAsync(It.IsAny<TrainingSampleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Record_SendsNothingWhenTheConsentWasGivenToAnOlderVersion()
    {
        UserConsentedTo(ConsentVersions.Training - 1);

        await Service().RecordAsync(_userId, _scanId, "X", Lines, 1_000, null, null);

        _ai.Verify(a => a.RecordTrainingSampleAsync(It.IsAny<TrainingSampleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Record_SendsNothingForAnUnknownUserOrAnEmptyBill()
    {
        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        await Service().RecordAsync(_userId, _scanId, "X", Lines, 1_000, null, null);

        UserConsentedTo(ConsentVersions.Training);
        await Service().RecordAsync(_userId, _scanId, "X", [], 1_000, null, null);

        _ai.Verify(a => a.RecordTrainingSampleAsync(It.IsAny<TrainingSampleRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Record_NeverThrowsWhenTheAiServiceOrTheUserLookupFails()
    {
        UserConsentedTo(ConsentVersions.Training);
        _ai.Setup(a => a.RecordTrainingSampleAsync(It.IsAny<TrainingSampleRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var act = () => Service().RecordAsync(_userId, _scanId, "X", Lines, 1_000, null, null);
        await act.Should().NotThrowAsync();

        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        await act.Should().NotThrowAsync();
    }

    // ---- gắn nhãn cuối lúc lưu / sửa

    [Fact]
    public async Task Label_SendsTheFinalSlugOfASystemCategory()
    {
        UserConsentedTo(ConsentVersions.Training);

        await Service().LabelAsync(_userId, _scanId, System("shopping"));

        _ai.Verify(a => a.LabelTrainingSampleAsync(
            It.Is<TrainingLabelRequest>(r => r.ScanId == _scanId && r.UserId == _userId && r.FinalSlug == "shopping"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Label_SendsNothingWhenConsentWasWithdrawnBetweenTheScanAndTheSave()
    {
        UserConsentedTo(null);

        await Service().LabelAsync(_userId, _scanId, System("food"));

        _ai.Verify(a => a.LabelTrainingSampleAsync(It.IsAny<TrainingLabelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Label_SkipsACustomCategoryIncomeAndNoCategory()
    {
        UserConsentedTo(ConsentVersions.Training);
        var mine = new Category { Id = Guid.NewGuid(), Slug = "cafe-cua-toi", Name = "Cà phê của tôi", UserId = Guid.NewGuid() };

        await Service().LabelAsync(_userId, _scanId, mine);
        await Service().LabelAsync(_userId, _scanId, System("income"));
        await Service().LabelAsync(_userId, _scanId, null);

        _ai.Verify(a => a.LabelTrainingSampleAsync(It.IsAny<TrainingLabelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- xóa dữ liệu đã đóng góp

    [Fact]
    public async Task Erase_CallsTheAiServiceEvenThoughConsentIsAlreadyWithdrawn()
    {
        UserConsentedTo(null);  // rút đồng ý xong mới xóa: không được đòi đang đồng ý

        await Service().EraseAsync(_userId);

        _ai.Verify(a => a.EraseTrainingSamplesAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Erase_ReportsFailureInsteadOfThrowingWhenTheAiServiceFails()
    {
        _ai.Setup(a => a.EraseTrainingSamplesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        (await Service().EraseAsync(_userId)).Should().BeFalse();  // người gọi phải biết dữ liệu CHƯA bị xóa để không báo đã xóa
    }

    [Fact]
    public async Task Erase_ReportsSuccess()
    {
        (await Service().EraseAsync(_userId)).Should().BeTrue();
    }

    [Fact]
    public async Task Label_NeverThrowsWhenTheAiServiceFails()
    {
        UserConsentedTo(ConsentVersions.Training);
        _ai.Setup(a => a.LabelTrainingSampleAsync(It.IsAny<TrainingLabelRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => Service().LabelAsync(_userId, _scanId, System("food"));

        await act.Should().NotThrowAsync();
    }
}
