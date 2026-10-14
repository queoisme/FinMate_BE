using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Nối dịch vụ mẫu huấn luyện vào ba chỗ: quét (gửi mẫu chờ nhãn cho cả bill một nhóm), lưu và sửa (gắn nhãn cuối).
/// Bill tách nhiều nhóm và danh sách đơn không gửi mẫu; giao dịch không từ hóa đơn không gắn nhãn.
/// </summary>
public class TrainingSampleHandlerTests
{
    private static readonly DateTimeOffset BillDate = new(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7));
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<IReceiptScanRepository> _scans = new();
    private readonly Mock<ITrainingSampleService> _training = new();

    private static Category System(string slug) => new() { Id = Guid.NewGuid(), Slug = slug, Name = slug, UserId = null };

    // ------------------------------------------------------------ quét

    private readonly Mock<IOcrServiceClient> _ocr = new();
    private readonly Mock<IUserRepository> _users = new();
    private Guid _savedScanId;

    private ScanReceiptCommandHandler ScanHandler(bool splitEnabled, OcrServiceResponse ocr)
    {
        _ocr.Setup(o => o.IsConfigured).Returns(true);
        _ocr.Setup(o => o.AnalyzeAsync(It.IsAny<OcrServiceRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(ocr);
        _scans.Setup(r => r.AddAsync(It.IsAny<ReceiptScan>(), It.IsAny<CancellationToken>()))
            .Callback<ReceiptScan, CancellationToken>((scan, _) => { scan.Id = _savedScanId = Guid.NewGuid(); })
            .Returns(Task.CompletedTask);
        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, AiConsentVersion = ConsentVersions.Ai });
        _ai.Setup(a => a.ClassifyBillAsync(It.IsAny<BillClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillClassifyResponse("food", "dining", 0.8, "brand"));
        _ai.Setup(a => a.ClassifyItemsAsync(It.IsAny<ClassifyItemsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClassifyItemsRequest r, CancellationToken _) =>
                new ClassifyItemsResponse(r.Items.Select((_, i) => new ClassifiedItem(i, i == 0 ? "food" : "shopping", "rule")).ToList(), false));
        return new ScanReceiptCommandHandler(
            _ai.Object, _ocr.Object, _scans.Object, new ReceiptOptions(splitEnabled), _users.Object,
            new Mock<ITransactionRepository>().Object, NullLogger<ScanReceiptCommandHandler>.Instance, trainingSamples: _training.Object);
    }

    private ScanReceiptCommand Scan() => new(_userId, new byte[1024], "hoa-don.jpg", "image/jpeg");

    private static OcrServiceResponse Bill(bool withTwoGroups = false, IReadOnlyList<OcrOrder>? orders = null) => new(
        true, 82_000, "CIRCLE K", "Circle K", ["CIRCLE K", "Tong tien: 82.000"], "shopping", "brand", false, BillDate, "text",
        withTwoGroups
            ? [new OcrItem("Ca phe sua", 30_000, null, false), new OcrItem("Dau goi", 52_000, null, false)]
            : [new OcrItem("Ca phe sua", 82_000, null, false)],
        ItemsOk: withTwoGroups, Orders: orders);

    [Fact]
    public async Task AWholeBillScanSendsOnePendingSampleWithTheScanIdAndThePrediction()
    {
        var dto = await ScanHandler(splitEnabled: false, Bill()).HandleAsync(Scan());

        dto.ScanId.Should().Be(_savedScanId);
        _training.Verify(t => t.RecordAsync(
            _userId, _savedScanId, "Circle K", It.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "CIRCLE K", "Tong tien: 82.000" })),
            82_000L, "food", "brand", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASplitScanSendsNoSample()
    {
        var dto = await ScanHandler(splitEnabled: true, Bill(withTwoGroups: true)).HandleAsync(Scan());

        dto.Groups.Should().NotBeNull();
        _training.Verify(t => t.RecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnOrderListScanSendsNoSample()
    {
        var orders = new List<OcrOrder> { new("Shop A", "Shop A", 50_000, "Ao", "shopping"), new("Shop B", "Shop B", 32_000, "Quan", "shopping") };

        await ScanHandler(splitEnabled: false, Bill(orders: orders)).HandleAsync(Scan());

        _training.Verify(t => t.RecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnUnreadableScanSendsNoSample()
    {
        await ScanHandler(splitEnabled: false, Bill() with { Readable = false }).HandleAsync(Scan());

        _training.Verify(t => t.RecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<long?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------ lưu

    private CreateManualTransactionCommandHandler CreateHandler(Category category, FinancialAccount account)
    {
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(r => r.GetByIdAsync(account.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        var budget = new Mock<IBudgetPeriodService>();
        budget.Setup(b => b.ApplyDeltaAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new CreateManualTransactionCommandHandler(
            new Mock<ITransactionRepository>().Object, accounts.Object, categories.Object, budget.Object,
            new Mock<IBudgetAlertNotifier>().Object, new Mock<IGamificationService>().Object, _ai.Object,
            _scans.Object, new Mock<ICacheService>().Object, NullLogger<CreateManualTransactionCommandHandler>.Instance,
            new CreateManualTransactionCommandValidator(), trainingSamples: _training.Object);
    }

    private CreateManualTransactionCommand Create(FinancialAccount account, Category category, TransactionSource source, Guid? scanId)
        => new(_userId, account.Id, category.Id, 16_000, TransactionType.Debit, DateTimeOffset.UtcNow, "Highlands Coffee", null, null, source, scanId);

    private static FinancialAccount Account(Guid userId) => new() { Id = Guid.NewGuid(), UserId = userId, BalanceCents = 100_000 };

    private void VerifyNeverLabelled() => _training.Verify(
        t => t.LabelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Category?>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task SavingAReceiptTransactionLinkedToAnUnsplitScanLabelsTheSampleWithTheChosenCategory()
    {
        var account = Account(_userId);
        var category = System("entertainment");
        var scanId = Guid.NewGuid();
        _scans.Setup(r => r.IsSplitAsync(scanId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await CreateHandler(category, account).HandleAsync(Create(account, category, TransactionSource.Receipt, scanId));

        _training.Verify(t => t.LabelAsync(_userId, scanId, category, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingFromASplitScanLabelsNothing()
    {
        var account = Account(_userId);
        var category = System("food");
        var scanId = Guid.NewGuid();
        _scans.Setup(r => r.IsSplitAsync(scanId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await CreateHandler(category, account).HandleAsync(Create(account, category, TransactionSource.Receipt, scanId));

        VerifyNeverLabelled();
    }

    [Theory]
    [InlineData(TransactionSource.Manual)]
    [InlineData(TransactionSource.Voice)]
    public async Task OtherChannelsLabelNothingEvenWithAScanId(TransactionSource source)
    {
        var account = Account(_userId);
        var category = System("food");

        await CreateHandler(category, account).HandleAsync(Create(account, category, source, Guid.NewGuid()));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task AReceiptWithoutAScanIdLabelsNothing()
    {
        var account = Account(_userId);
        var category = System("food");

        await CreateHandler(category, account).HandleAsync(Create(account, category, TransactionSource.Receipt, null));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task WhenTheSplitCheckFailsTheSaveSucceedsAndLabelsNothing()
    {
        var account = Account(_userId);
        var category = System("food");
        _scans.Setup(r => r.IsSplitAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await CreateHandler(category, account).HandleAsync(Create(account, category, TransactionSource.Receipt, Guid.NewGuid()));

        result.AlreadyExisted.Should().BeFalse();
        VerifyNeverLabelled();
    }

    // ------------------------------------------------------------ sửa

    private (UpdateTransactionCommandHandler Handler, Transaction Transaction) UpdateSetup(TransactionSource source, Category oldCategory, Category newCategory)
    {
        var account = Account(_userId);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, FinancialAccountId = account.Id, CategoryId = oldCategory.Id, Category = oldCategory,
            AmountCents = 16_000, TransactionType = TransactionType.Debit, Source = source, Status = TransactionStatus.Draft,
            MerchantName = "Highlands Coffee", TransactedAt = DateTimeOffset.UtcNow,
        };
        var transactions = new Mock<ITransactionRepository>();
        transactions.Setup(r => r.GetByIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(r => r.GetByIdAsync(newCategory.Id, It.IsAny<CancellationToken>())).ReturnsAsync(newCategory);
        var handler = new UpdateTransactionCommandHandler(
            transactions.Object, new Mock<IFinancialAccountRepository>().Object, categories.Object, _ai.Object,
            new Mock<IBudgetPeriodService>().Object, new Mock<IBudgetAlertNotifier>().Object, new Mock<ICacheService>().Object,
            _scans.Object, NullLogger<UpdateTransactionCommandHandler>.Instance, new UpdateTransactionCommandValidator(), trainingSamples: _training.Object);
        return (handler, transaction);
    }

    private UpdateTransactionCommand Update(Transaction t, Category category)
        => new(_userId, t.Id, t.FinancialAccountId, null, category.Id, t.AmountCents, t.TransactionType, t.TransactedAt, t.MerchantName, null);

    [Fact]
    public async Task CorrectingTheCategoryRelabelsTheSampleOfTheLinkedUnsplitScan()
    {
        var newCategory = System("entertainment");
        var (handler, transaction) = UpdateSetup(TransactionSource.Receipt, System("food"), newCategory);
        var scanId = Guid.NewGuid();
        _scans.Setup(r => r.GetLinkedScanIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scanId);
        _scans.Setup(r => r.IsLinkedToSplitScanAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await handler.HandleAsync(Update(transaction, newCategory));

        _training.Verify(t => t.LabelAsync(_userId, scanId, newCategory, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CorrectingATransactionThatIsNotLinkedToAScanRelabelsNothing()
    {
        var newCategory = System("entertainment");
        var (handler, transaction) = UpdateSetup(TransactionSource.Receipt, System("food"), newCategory);
        _scans.Setup(r => r.GetLinkedScanIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await handler.HandleAsync(Update(transaction, newCategory));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task CorrectingATransactionOfASplitScanRelabelsNothing()
    {
        var newCategory = System("entertainment");
        var (handler, transaction) = UpdateSetup(TransactionSource.Receipt, System("food"), newCategory);
        _scans.Setup(r => r.GetLinkedScanIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        _scans.Setup(r => r.IsLinkedToSplitScanAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await handler.HandleAsync(Update(transaction, newCategory));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task EditingWithoutChangingTheCategoryRelabelsNothing()
    {
        var category = System("food");
        var (handler, transaction) = UpdateSetup(TransactionSource.Receipt, category, category);
        _scans.Setup(r => r.GetLinkedScanIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        await handler.HandleAsync(Update(transaction, category));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task CorrectingAManualTransactionRelabelsNothing()
    {
        var newCategory = System("entertainment");
        var (handler, transaction) = UpdateSetup(TransactionSource.Manual, System("food"), newCategory);
        _scans.Setup(r => r.GetLinkedScanIdAsync(transaction.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        await handler.HandleAsync(Update(transaction, newCategory));

        VerifyNeverLabelled();
    }

    [Fact]
    public async Task WhenTheScanLookupFailsTheEditSucceedsAndRelabelsNothing()
    {
        var newCategory = System("entertainment");
        var (handler, transaction) = UpdateSetup(TransactionSource.Receipt, System("food"), newCategory);
        _scans.Setup(r => r.GetLinkedScanIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var dto = await handler.HandleAsync(Update(transaction, newCategory));

        dto.Id.Should().Be(transaction.Id);
        VerifyNeverLabelled();
    }
}
