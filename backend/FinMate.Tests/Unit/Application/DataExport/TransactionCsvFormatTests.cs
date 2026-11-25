using FinMate.Application.DataExport;
using FinMate.Application.Reports;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>
/// Phase 91 (BR-EXP-04/05, D1, D5, D6, D11, S2): một giao dịch thành một dòng CSV. "Tính vào chi tiêu" và số có dấu phải khớp đúng luật báo cáo
/// (<see cref="NetFlow"/>, loại <c>loan</c>, <c>Transfer</c>, chỉ <c>Confirmed</c>), nên tổng cột 17 của một tháng bằng <c>totalSpentCents</c>.
/// </summary>
public class TransactionCsvFormatTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 17, 30, 0, TimeSpan.Zero);   // 00:30 ngày 25/11 giờ VN

    private static TransactionExportRow Row(
        TransactionType type = TransactionType.Debit, long amount = 45_000, TransactionStatus status = TransactionStatus.Confirmed,
        TransactionRelation? relation = null, bool isReversed = false, string? categorySlug = "food") => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        TransactedAt = At,
        Type = type,
        AmountCents = amount,
        AccountName = "Vietcombank",
        CategoryName = "Ăn uống",
        CategorySlug = categorySlug,
        MerchantName = "Highlands",
        Description = "cà phê",
        Source = TransactionSource.Manual,
        Status = status,
        Relation = relation,
        IsReversed = isReversed,
    };

    private static Dictionary<string, string?> Cells(TransactionExportRow row, bool deletedFile = false)
    {
        var header = TransactionCsvFormat.Header(deletedFile);
        var cells = TransactionCsvFormat.ToCells(row, deletedFile);
        cells.Should().HaveCount(header.Count);
        return header.Zip(cells).ToDictionary(p => p.First, p => p.Second.NumberValue?.ToString() ?? p.Second.TextValue);
    }

    [Fact]
    public void TheHeader_IsTheFixedVietnameseColumnList()
    {
        TransactionCsvFormat.Header(deletedFile: false).Should().Equal(
            "Ngày", "Thời điểm", "Loại", "Số tiền", "Ví", "Ví đích", "Danh mục", "Nơi chi", "Ghi chú", "Nguồn", "Trạng thái", "Quan hệ",
            "Liên quan đến", "Gộp từ", "Nhóm", "Tính vào chi tiêu", "Số tiền tính vào chi tiêu", "Mã giao dịch");
        TransactionCsvFormat.Header(deletedFile: true).Should().EndWith("Đã xoá lúc");
    }

    [Fact]
    public void DateAndTime_AreInVietnamTime_WithTheOffset()
    {
        var cells = Cells(Row());

        cells["Ngày"].Should().Be("2026-11-25");
        cells["Thời điểm"].Should().Be("2026-11-25T00:30:00+07:00");
    }

    [Fact]
    public void APlainExpense_IsCounted_WithAPositiveEffect()
    {
        var cells = Cells(Row());

        cells["Loại"].Should().Be("Chi");
        cells["Số tiền"].Should().Be("45000");
        cells["Trạng thái"].Should().Be("Đã xác nhận");
        cells["Quan hệ"].Should().BeNull();
        cells["Tính vào chi tiêu"].Should().Be("Có");
        cells["Số tiền tính vào chi tiêu"].Should().Be("45000");
        cells["Mã giao dịch"].Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Income_IsNotSpending()
    {
        var cells = Cells(Row(TransactionType.Credit, 10_000_000, categorySlug: "income"));

        cells["Loại"].Should().Be("Thu");
        cells["Tính vào chi tiêu"].Should().Be("Không");
        cells["Số tiền tính vào chi tiêu"].Should().Be("0");
    }

    [Fact]
    public void ARefund_LowersSpending_AndPointsAtTheOriginal()
    {
        var original = Guid.NewGuid();
        var cells = Cells(Row(TransactionType.Credit, 20_000, relation: TransactionRelation.Refund) with { RelatedTransactionId = original });

        cells["Quan hệ"].Should().Be("Hoàn tiền");
        cells["Liên quan đến"].Should().Be(original.ToString());
        cells["Tính vào chi tiêu"].Should().Be("Có");
        cells["Số tiền tính vào chi tiêu"].Should().Be("-20000");
    }

    [Fact]
    public void AReversalPair_IsLeftOutOfSpending_OnBothSides()
    {
        var reversal = Guid.NewGuid();
        var original = Cells(Row(isReversed: true) with { ReversedByTransactionId = reversal });
        var undo = Cells(Row(TransactionType.Credit, relation: TransactionRelation.Reversal) with { RelatedTransactionId = Guid.NewGuid() });

        original["Quan hệ"].Should().Be("Đã bị đảo");
        original["Liên quan đến"].Should().Be(reversal.ToString());
        undo["Quan hệ"].Should().Be("Đảo giao dịch");
        new[] { original, undo }.Should().AllSatisfy(c =>
        {
            c["Tính vào chi tiêu"].Should().Be("Không");
            c["Số tiền tính vào chi tiêu"].Should().Be("0");
        });
    }

    [Fact]
    public void LendingMoney_IsNotSpending_ButTheSplitInterestIs()
    {
        var loan = Guid.NewGuid();
        var principal = Cells(Row(categorySlug: "loan") with { LoanId = loan });
        var interest = Cells(Row(amount: 60_000, categorySlug: "other") with { LoanId = loan, IsLoanInterest = true });

        principal["Quan hệ"].Should().Be("Vay nợ");
        principal["Liên quan đến"].Should().Be(loan.ToString());
        principal["Số tiền tính vào chi tiêu"].Should().Be("0");
        interest["Quan hệ"].Should().Be("Lãi vay");
        interest["Số tiền tính vào chi tiêu"].Should().Be("60000");
    }

    [Fact]
    public void ALoanTransactionWithoutABookEntry_IsStillLeftOutOfSpending()
    {
        Cells(Row(categorySlug: "loan"))["Tính vào chi tiêu"].Should().Be("Không");
    }

    [Fact]
    public void AnOwnWalletTransfer_ShowsTheTargetWallet_AndIsNotSpending()
    {
        var cells = Cells(Row(TransactionType.Transfer, 2_000_000, categorySlug: null) with { CounterAccountName = "MoMo", CategoryName = null });

        cells["Loại"].Should().Be("Chuyển ví");
        cells["Ví đích"].Should().Be("MoMo");
        cells["Tính vào chi tiêu"].Should().Be("Không");
    }

    [Fact]
    public void AGroupFundLine_HasNoTargetWallet_AndPointsAtTheFundEntry()
    {
        var entry = Guid.NewGuid();
        var cells = Cells(Row(TransactionType.Transfer, 500_000, categorySlug: null) with { GroupFundEntryId = entry, GroupName = "Phòng trọ" });

        cells["Quan hệ"].Should().Be("Quỹ nhóm");
        cells["Liên quan đến"].Should().Be(entry.ToString());
        cells["Ví đích"].Should().BeNull();
        cells["Nhóm"].Should().Be("Phòng trọ");
        cells["Số tiền tính vào chi tiêu"].Should().Be("0");
    }

    [Fact]
    public void ADraft_IsNeverCounted_AndTheStatusHasOnlyTwoValues()
    {
        var cells = Cells(Row(status: TransactionStatus.Draft));

        cells["Trạng thái"].Should().Be("Chờ duyệt");
        cells["Tính vào chi tiêu"].Should().Be("Không");
        Enum.GetValues<TransactionStatus>().Should().HaveCount(2, "S2: chỉ có Đã xác nhận / Chờ duyệt");
    }

    [Fact]
    public void ASuspectedDuplicate_PointsAtTheCandidate_ButLosesToAStrongerRelation()
    {
        var candidate = Guid.NewGuid();
        var loan = Guid.NewGuid();

        Cells(Row() with { DuplicateStatus = DuplicateStatus.Suspected, DuplicateOfTransactionId = candidate })["Quan hệ"].Should().Be("Nghi trùng");
        Cells(Row(categorySlug: "loan") with { LoanId = loan, DuplicateStatus = DuplicateStatus.Suspected, DuplicateOfTransactionId = candidate })
            ["Liên quan đến"].Should().Be(loan.ToString(), "D11: một cột theo thứ tự ưu tiên");
        Cells(Row() with { DuplicateStatus = DuplicateStatus.Dismissed, DuplicateOfTransactionId = candidate })["Quan hệ"].Should().BeNull();
    }

    [Theory]
    [InlineData(TransactionSource.Manual, "Tay")]
    [InlineData(TransactionSource.Notification, "Thông báo")]
    [InlineData(TransactionSource.Voice, "Giọng nói")]
    [InlineData(TransactionSource.Receipt, "Hoá đơn")]
    [InlineData(TransactionSource.Support, "Hỗ trợ")]
    public void EverySource_HasAVietnameseName(TransactionSource source, string expected)
    {
        Cells(Row() with { Source = source })["Nguồn"].Should().Be(expected);
    }

    [Fact]
    public void MergedSources_AreListedInVietnamese()
    {
        Cells(Row() with { MergedSources = ["notification", "manual"] })["Gộp từ"].Should().Be("Thông báo; Tay");
    }

    [Fact]
    public void TheDeletedFile_HasTheDeletionTime_PointsAMergedRowAtTheSurvivor_AndCountsNothing()
    {
        var survivor = Guid.NewGuid();
        var cells = Cells(Row() with { DeletedAt = At, MergedIntoTransactionId = survivor }, deletedFile: true);

        cells["Đã xoá lúc"].Should().Be("2026-11-25T00:30:00+07:00");
        cells["Quan hệ"].Should().Be("Đã gộp vào");
        cells["Liên quan đến"].Should().Be(survivor.ToString());
        cells["Tính vào chi tiêu"].Should().Be("Không");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeletedRow_CountsZero_WhileALiveRowInTheSameFileKeepsItsSpending(bool deletedFile)
    {
        Cells(Row() with { DeletedAt = At }, deletedFile)["Số tiền tính vào chi tiêu"].Should().Be("0");

        var live = Cells(Row(), deletedFile);
        live["Số tiền tính vào chi tiêu"].Should().Be("45000");
        if (deletedFile)
        {
            live["Đã xoá lúc"].Should().BeNullOrEmpty();
        }
    }

    [Fact]
    public void NoBankInternals_HaveAColumn()
    {
        TransactionCsvFormat.Header(deletedFile: true).Should().NotContain(h =>
            h.Contains("số dư", StringComparison.OrdinalIgnoreCase) || h.Contains("thông báo gốc", StringComparison.OrdinalIgnoreCase)
            || h.Contains("tin cậy", StringComparison.OrdinalIgnoreCase));
        typeof(TransactionExportRow).GetProperties().Select(p => p.Name).Should().NotContain(new[]
        {
            "BalanceAfterCents", "ClientRequestId", "NotificationLogId", "NotificationBody", "ProviderReference", "Confidence",
        });
    }
}
