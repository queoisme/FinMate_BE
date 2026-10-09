using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class ChatCommandHandlerTests
{
    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<ICreateManualTransactionCommandHandler> _create = new();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IReportRepository> _reports = new();
    private readonly ChatCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Category _food = new() { Id = Guid.NewGuid(), Slug = "food" };

    public ChatCommandHandlerTests()
    {
        _handler = new ChatCommandHandler(
            _ai.Object, _create.Object, _accounts.Object, _categories.Object, _reports.Object, new ChatCommandValidator());

        _accounts
            .Setup(a => a.GetListByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialAccount> { new() { Id = _accountId, UserId = _userId } });
        _categories
            .Setup(c => c.GetSystemBySlugAsync("food", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_food);
        _create
            .Setup(c => c.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateManualTransactionCommand c, CancellationToken _) => new TransactionCreationResult(
                new TransactionDto(
                    Guid.NewGuid(), c.FinancialAccountId, null, c.CategoryId, "Ăn uống", c.AmountCents, "Debit", "Manual",
                    "Confirmed", c.MerchantName, c.Description, c.TransactedAt, null, DateTimeOffset.UtcNow),
                false));
    }

    private void AiReturns(ChatParseResponse response) => _ai
        .Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

    private static ChatParseResponse Entry(
        string kind = "expense", long amount = 200_000, string? category = "food", bool auto = true, string? incomeType = null)
        => new("add_expense", null, new ChatParseEntry(kind, amount, "đi ăn", null, category, incomeType, auto, false), null, 5);

    private ChatCommand Command(string text = "đi ăn 200k", Guid? accountId = null) => new(_userId, text, accountId);

    private void NothingWasCreated() => _create.Verify(
        c => c.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()), Times.Never);

    // ------------------------------------------------------------ ghi ngay

    [Fact]
    public async Task AConfidentExpenseIsSavedStraightAway()
    {
        AiReturns(Entry());

        var result = await _handler.HandleAsync(Command());

        result.Action.Should().Be(ChatActions.Saved);
        result.Transaction!.AmountCents.Should().Be(200_000);
        _create.Verify(c => c.HandleAsync(
            It.Is<CreateManualTransactionCommand>(x =>
                x.UserId == _userId && x.FinancialAccountId == _accountId && x.CategoryId == _food.Id &&
                x.AmountCents == 200_000 && x.TransactionType == TransactionType.Debit && x.Source == TransactionSource.Manual),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithSeveralAccountsAndNoChoiceItAsksInsteadOfGuessing()
    {
        _accounts
            .Setup(a => a.GetListByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialAccount> { new() { Id = _accountId }, new() { Id = Guid.NewGuid() } });
        AiReturns(Entry());

        var result = await _handler.HandleAsync(Command());

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.MissingFields.Should().Equal(ChatMissingFields.Account);
        NothingWasCreated();
    }

    [Fact]
    public async Task AnAccountThatIsNotTheUsersIsNeverUsed()
    {
        _accounts
            .Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FinancialAccount?)null);
        AiReturns(Entry());

        var result = await _handler.HandleAsync(Command(accountId: Guid.NewGuid()));

        result.Action.Should().Be(ChatActions.Confirm);
        NothingWasCreated();
    }

    [Fact]
    public async Task AChosenAccountOfTheUserIsUsed()
    {
        var other = Guid.NewGuid();
        _accounts
            .Setup(a => a.GetByIdAsync(other, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialAccount { Id = other, UserId = _userId });
        AiReturns(Entry());

        await _handler.HandleAsync(Command(accountId: other));

        _create.Verify(c => c.HandleAsync(
            It.Is<CreateManualTransactionCommand>(x => x.FinancialAccountId == other), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACategoryTheDatabaseDoesNotHaveIsNeverSavedUncategorised()
    {
        AiReturns(Entry(category: "bills"));  // slug hợp lệ nhưng chưa seed

        var result = await _handler.HandleAsync(Command());

        result.Action.Should().Be(ChatActions.Confirm);
        NothingWasCreated();
    }

    // ------------------------------------------------------------ hỏi xác nhận

    [Fact]
    public async Task AnExpenseTheAiIsNotSureAboutBecomesADraft()
    {
        AiReturns(Entry(auto: false, amount: 3_000_000));

        var result = await _handler.HandleAsync(Command("mua áo 3 triệu"));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Transaction.Should().BeNull();
        result.Draft!.Kind.Should().Be("expense");
        result.Draft.AmountCents.Should().Be(3_000_000);
        result.Draft.TransactionType.Should().Be("debit");
        result.Draft.CategorySlug.Should().Be("food");
        result.Draft.SuggestedFinancialAccountId.Should().Be(_accountId);
        result.Draft.MissingFields.Should().BeEmpty();
        NothingWasCreated();
    }

    [Fact]
    public async Task AnExpenseWithoutACategoryAsksForOne()
    {
        AiReturns(Entry(category: null, auto: false));

        var result = await _handler.HandleAsync(Command("250k"));

        result.Draft!.MissingFields.Should().Equal(ChatMissingFields.Category);
    }

    [Fact]
    public async Task IncomeIsNeverSavedEvenIfTheAiSaysSo()
    {
        AiReturns(Entry(kind: "income", category: "income", auto: true, incomeType: "salary"));

        var result = await _handler.HandleAsync(Command("nhận lương 14 củ 5"));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.TransactionType.Should().Be("credit");
        result.Draft.IncomeType.Should().Be("salary");
        NothingWasCreated();
    }

    [Fact]
    public async Task ATransferNeedsTwoAccountsAndIsNeverSaved()
    {
        AiReturns(Entry(kind: "transfer", category: null, auto: true));

        var result = await _handler.HandleAsync(Command("chuyển 2 triệu sang tiết kiệm"));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.TransactionType.Should().BeNull();
        result.Draft.SuggestedFinancialAccountId.Should().BeNull();
        result.Draft.MissingFields.Should().Equal(ChatMissingFields.Accounts);
        NothingWasCreated();
    }

    // ------------------------------------------------------------ tổng hợp

    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset To = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7));

    private static ChatParseResponse Query(string metric, string? category = null)
        => new("ask_spending", null, null, new ChatParseQuery(metric, "tháng này", From, To, category, null), 5);

    private void MonthHas(long spent, long income, params CategorySpend[] byCategory)
    {
        _reports
            .Setup(r => r.GetDailySpendAsync(_userId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailySpendPoint> { new(new DateOnly(2026, 10, 5), spent, income, 3) });
        _reports
            .Setup(r => r.GetCategorySpendAsync(_userId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(byCategory.ToList());
    }

    [Fact]
    public async Task ExpenseTotalsAreComputedFromTheUsersOwnTransactions()
    {
        MonthHas(600_000, 0, new CategorySpend(null, "Ăn uống", "food", 300_000), new CategorySpend(null, "Mua sắm", "shopping", 300_000));
        AiReturns(Query("expense"));

        var result = await _handler.HandleAsync(Command("tháng này tôi tiêu bao nhiêu"));

        result.Action.Should().Be(ChatActions.Summary);
        result.Summary!.TotalCents.Should().Be(600_000);
        result.Summary.Items.Select(i => i.CategorySlug).Should().BeEquivalentTo("food", "shopping");
        result.Summary.Items.Sum(i => i.Share).Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task AnExpenseTotalCanBeNarrowedToOneCategory()
    {
        MonthHas(600_000, 0, new CategorySpend(null, "Ăn uống", "food", 300_000), new CategorySpend(null, "Mua sắm", "shopping", 300_000));
        AiReturns(Query("expense", "food"));

        var result = await _handler.HandleAsync(Command("tháng này tôi ăn uống bao nhiêu"));

        result.Summary!.TotalCents.Should().Be(300_000);
        result.Summary.Items.Should().ContainSingle().Which.CategorySlug.Should().Be("food");
    }

    [Fact]
    public async Task BalanceIsIncomeMinusExpense()
    {
        MonthHas(200_000, 15_000_000);
        AiReturns(Query("balance"));

        var summary = (await _handler.HandleAsync(Command("tháng này còn lại bao nhiêu"))).Summary!;

        summary.IncomeCents.Should().Be(15_000_000);
        summary.ExpenseCents.Should().Be(200_000);
        summary.LeftCents.Should().Be(14_800_000);
        summary.TotalCents.Should().Be(14_800_000);
        summary.Items.Should().BeEmpty();
    }

    // ------------------------------------------------------------ còn lại

    [Fact]
    public async Task SmallTalkIsJustAReply()
    {
        AiReturns(new ChatParseResponse("greet", "Chào bạn!", null, null, 3));

        var result = await _handler.HandleAsync(Command("xin chào"));

        result.Action.Should().Be(ChatActions.Reply);
        result.ReplyText.Should().Be("Chào bạn!");
        result.Transaction.Should().BeNull();
        result.Draft.Should().BeNull();
        result.Summary.Should().BeNull();
    }

    [Fact]
    public async Task EmptyTextNeverReachesTheAiService()
    {
        var act = () => _handler.HandleAsync(Command("   "));

        await act.Should().ThrowAsync<ValidationException>();
        _ai.Verify(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AiServiceDownIsNotSwallowed()
    {
        _ai.Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var act = () => _handler.HandleAsync(Command());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
        NothingWasCreated();
    }
}
