using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Exports;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Exports;

public class TransactionExportTests
{
    private static readonly Guid Cash = Guid.NewGuid();
    private static readonly Guid Bank = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, string> Names = new Dictionary<Guid, string> { [Cash] = "Ví tiền mặt", [Bank] = "BIDV" };

    private static Transaction Tx(TransactionType type, long amount, string? description = null, string? merchant = null, Guid? counter = null)
        => new()
        {
            Id = Guid.NewGuid(), FinancialAccountId = Cash, CounterAccountId = counter, TransactionType = type, AmountCents = amount,
            Description = description, MerchantName = merchant,
            TransactedAt = new DateTimeOffset(2026, 11, 27, 3, 5, 0, TimeSpan.Zero),   // 10:05 giờ Việt Nam
            Category = new Category { Name = "Ăn uống" },
        };

    [Fact]
    public void Build_WritesBomHeaderAndSignedAmountsInVietnamTime()
    {
        var csv = TransactionCsvBuilder.Build([Tx(TransactionType.Debit, 45_000, "ăn phở"), Tx(TransactionType.Credit, 1_000_000, "lương")], Names, ',');

        csv.Should().StartWith("﻿Ngày giờ,Loại,Số tiền (đ),Danh mục,Ví,Ví đối ứng,Nơi nhận/Cửa hàng,Mô tả\r\n");
        csv.Should().Contain("27/11/2026 10:05,Chi tiêu,-45000,Ăn uống,Ví tiền mặt,,,ăn phở\r\n");
        csv.Should().Contain("27/11/2026 10:05,Thu nhập,1000000,Ăn uống,Ví tiền mặt,,,lương\r\n");
    }

    [Fact]
    public void Build_TransferShowsCounterWalletAndUnknownWalletIsMarked()
    {
        var csv = TransactionCsvBuilder.Build([Tx(TransactionType.Transfer, 500_000, counter: Bank), Tx(TransactionType.Transfer, 1, counter: Guid.NewGuid())], Names, ',');

        csv.Should().Contain("Chuyển tiền,500000,Ăn uống,Ví tiền mặt,BIDV,");
        csv.Should().Contain("(ví đã xoá)");
    }

    [Fact]
    public void Build_QuotesCellsWithDelimiterQuoteOrNewline()
    {
        var csv = TransactionCsvBuilder.Build([Tx(TransactionType.Debit, 1, "cà phê, bánh \"mì\"\nsáng")], Names, ',');

        csv.Should().Contain("\"cà phê, bánh \"\"mì\"\"\nsáng\"");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+84 912")]
    [InlineData("-cmd")]
    [InlineData("@SUM(A1)")]
    public void Build_NeutralizesCellsExcelWouldRunAsFormulas(string description)
    {
        var csv = TransactionCsvBuilder.Build([Tx(TransactionType.Debit, 1, description)], Names, ';');

        csv.Should().Contain(";" + TransactionCsvBuilder.Escape("'" + description, ';') + "\r\n");
    }

    [Fact]
    public void Build_SemicolonModeDoesNotQuoteCommas()
    {
        var csv = TransactionCsvBuilder.Build([Tx(TransactionType.Debit, 1, "a, b")], Names, ';');

        csv.Should().Contain(";a, b\r\n");
    }

    [Fact]
    public void DelimiterOf_DefaultsToComma()
    {
        TransactionCsvBuilder.DelimiterOf(null).Should().Be(',');
        TransactionCsvBuilder.DelimiterOf("semicolon").Should().Be(';');
        TransactionCsvBuilder.DelimiterOf("SEMICOLON").Should().Be(';');
    }

    private static (TransactionExportService Service, Mock<ITransactionRepository> Repo) Service(params Transaction[] rows)
    {
        var repo = new Mock<ITransactionRepository>();
        repo.Setup(r => r.GetListAsync(It.IsAny<TransactionListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionListResult(rows, null));
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(a => a.GetListByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FinancialAccount { Id = Cash, AccountName = "Ví tiền mặt" }]);
        return (new TransactionExportService(repo.Object, accounts.Object), repo);
    }

    [Fact]
    public async Task Export_ReadsOnlyConfirmedTransactionsOfThatUserInVietnamDayRange()
    {
        var (service, repo) = Service(Tx(TransactionType.Debit, 1));
        var user = Guid.NewGuid();

        await service.ExportCsvAsync(user, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null, null);

        repo.Verify(r => r.GetListAsync(
            It.Is<TransactionListFilter>(f =>
                f.UserId == user && f.Status == TransactionStatus.Confirmed
                && f.FromDate == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7))
                && f.ToDate == new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(7)).AddTicks(-1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(2026, 11, 30, 2026, 11, 1)]
    [InlineData(1, 1, 1, 2026, 11, 1)]
    public async Task Export_RejectsMissingOrReversedRange(int fy, int fm, int fd, int ty, int tm, int td)
    {
        var (service, _) = Service();

        var act = () => service.ExportCsvAsync(Guid.NewGuid(), new DateOnly(fy, fm, fd), new DateOnly(ty, tm, td), null, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ExportErrorCodes.InvalidRange);
    }

    [Fact]
    public async Task Export_AboveTheRowCap_IsRefused()
    {
        var rows = Enumerable.Range(0, TransactionExportService.MaxRows + 1).Select(_ => Tx(TransactionType.Debit, 1)).ToArray();
        var (service, _) = Service(rows);

        var act = () => service.ExportCsvAsync(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(ExportErrorCodes.TooLarge);
    }
}
