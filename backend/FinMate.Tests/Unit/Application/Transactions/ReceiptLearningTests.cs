using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Dạy lại bộ nhớ cửa hàng: người dùng chọn danh mục cho một khoản chi từ hóa đơn (lúc lưu hoặc lúc sửa) thì AI Service nhớ cho lần quét sau.
/// Chỉ khi nguồn là hóa đơn, có tên cửa hàng, và danh mục là danh mục hệ thống.
/// </summary>
public class ReceiptLearningTests
{
    private static Category System(string slug) => new() { Id = Guid.NewGuid(), Slug = slug, Name = slug, UserId = null };

    [Theory]
    [InlineData(TransactionSource.Receipt, "Highlands Coffee", true)]
    [InlineData(TransactionSource.Manual, "Highlands Coffee", false)]
    [InlineData(TransactionSource.Voice, "Highlands Coffee", false)]
    [InlineData(TransactionSource.Notification, "Highlands Coffee", false)]
    [InlineData(TransactionSource.Receipt, null, false)]
    [InlineData(TransactionSource.Receipt, "   ", false)]
    public void OnlyReceiptsWithAShopNameAreLearned(TransactionSource source, string? merchant, bool expected)
        => ReceiptMemory.ShouldLearn(source, System("food"), merchant).Should().Be(expected);

    [Fact]
    public void ACategoryTheUserMadeThemselvesIsNeverSentBecauseTheAiDoesNotKnowItsSlug()
    {
        var mine = new Category { Id = Guid.NewGuid(), Slug = "cafe-cua-toi", Name = "Cà phê của tôi", UserId = Guid.NewGuid() };

        ReceiptMemory.ShouldLearn(TransactionSource.Receipt, mine, "Highlands Coffee").Should().BeFalse();
    }

    [Fact]
    public void NoCategoryAndIncomeAreNeverLearned()
    {
        ReceiptMemory.ShouldLearn(TransactionSource.Receipt, null, "Highlands Coffee").Should().BeFalse();
        ReceiptMemory.ShouldLearn(TransactionSource.Receipt, System("income"), "Highlands Coffee").Should().BeFalse();
    }

    // ------------------------------------------------------------ lúc lưu

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IAIServiceClient> _ai = new();

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
            new Mock<IReceiptScanRepository>().Object, new Mock<ICacheService>().Object, new CreateManualTransactionCommandValidator());
    }

    private CreateManualTransactionCommand Create(FinancialAccount account, Category category, TransactionSource source, string? merchant)
        => new(_userId, account.Id, category.Id, 16_000, TransactionType.Debit, DateTimeOffset.UtcNow, merchant, null, null, source);

    [Fact]
    public async Task SavingAReceiptExpenseTeachesTheAiTheCategoryTheUserChose()
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = _userId, BalanceCents = 100_000 };
        var category = System("entertainment");

        await CreateHandler(category, account).HandleAsync(Create(account, category, TransactionSource.Receipt, "Highlands Coffee"));

        _ai.Verify(a => a.LearnMerchantAsync(
            It.Is<LearnMerchantRequest>(r => r.UserId == _userId && r.MerchantName == "Highlands Coffee" && r.CategorySlug == "entertainment"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(TransactionSource.Manual)]
    [InlineData(TransactionSource.Voice)]
    public async Task OtherChannelsTeachNothing(TransactionSource source)
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = _userId, BalanceCents = 100_000 };
        var category = System("food");

        await CreateHandler(category, account).HandleAsync(Create(account, category, source, "Highlands Coffee"));

        _ai.Verify(a => a.LearnMerchantAsync(It.IsAny<LearnMerchantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------ lúc sửa

    private (UpdateTransactionCommandHandler Handler, Transaction Transaction, FinancialAccount Account) UpdateSetup(
        TransactionSource source, Category? oldCategory, Category newCategory)
    {
        var account = new FinancialAccount { Id = Guid.NewGuid(), UserId = _userId, BalanceCents = 100_000 };
        // Draft: chưa từng cộng trừ số dư hay ngân sách nên không cần dựng các phần đó, thứ cần kiểm là việc ghi nhớ.
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), UserId = _userId, FinancialAccountId = account.Id, CategoryId = oldCategory?.Id, Category = oldCategory,
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
            new UpdateTransactionCommandValidator());
        return (handler, transaction, account);
    }

    private UpdateTransactionCommand Update(Transaction t, Category category)
        => new(_userId, t.Id, t.FinancialAccountId, null, category.Id, t.AmountCents, t.TransactionType, t.TransactedAt, t.MerchantName, null);

    [Fact]
    public async Task CorrectingTheCategoryOfAReceiptExpenseTeachesTheAi()
    {
        var newCategory = System("entertainment");
        var (handler, transaction, _) = UpdateSetup(TransactionSource.Receipt, System("food"), newCategory);

        await handler.HandleAsync(Update(transaction, newCategory));

        _ai.Verify(a => a.LearnMerchantAsync(
            It.Is<LearnMerchantRequest>(r => r.UserId == _userId && r.MerchantName == "Highlands Coffee" && r.CategorySlug == "entertainment"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EditingAReceiptExpenseWithoutChangingTheCategoryTeachesNothing()
    {
        var category = System("food");
        var (handler, transaction, _) = UpdateSetup(TransactionSource.Receipt, category, category);

        await handler.HandleAsync(Update(transaction, category));

        _ai.Verify(a => a.LearnMerchantAsync(It.IsAny<LearnMerchantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CorrectingAManualTransactionTeachesNothing()
    {
        var newCategory = System("entertainment");
        var (handler, transaction, _) = UpdateSetup(TransactionSource.Manual, System("food"), newCategory);

        await handler.HandleAsync(Update(transaction, newCategory));

        _ai.Verify(a => a.LearnMerchantAsync(It.IsAny<LearnMerchantRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
