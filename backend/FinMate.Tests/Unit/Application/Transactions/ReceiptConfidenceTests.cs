using FinMate.Application.Transactions;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>Cùng công thức với AI Service (receipt_ocr._confidence): kết quả không đổi khi backend điều phối việc quét hóa đơn.</summary>
public class ReceiptConfidenceTests
{
    [Theory]
    [InlineData(false, false, false, false, false, 0.0)]    // không có tổng tiền: không có gì để tin
    [InlineData(true, false, false, false, false, 0.35)]    // chỉ đoán số lớn nhất
    [InlineData(true, true, false, false, false, 0.55)]     // có nhãn tổng tiền
    [InlineData(true, true, true, false, false, 0.70)]      // thêm tên cửa hàng
    [InlineData(true, true, true, true, false, 0.80)]       // thêm ngày: chạm trần
    [InlineData(true, false, true, true, false, 0.60)]
    public void ScoreFollowsTheEvidence(bool amount, bool label, bool merchant, bool date, bool review, double expected)
        => ReceiptConfidence.Compute(amount, label, merchant, date, review).Should().Be(expected);

    [Fact]
    public void NothingIsEverConfidentEnoughForOneTapConfirmation()
        => ReceiptConfidence.Compute(true, true, true, true, false).Should().BeLessThan(0.85);

    [Theory]
    [InlineData(true, true, true, true, 0.50)]
    [InlineData(true, false, false, false, 0.35)]   // thấp hơn trần xem-lại thì giữ nguyên
    public void ReceiptsFlaggedForReviewAreCapped(bool amount, bool label, bool merchant, bool date, double expected)
        => ReceiptConfidence.Compute(amount, label, merchant, date, needsReview: true).Should().Be(expected);
}
