using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Duplicates;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Phase 82 ở các kênh người dùng tự nhập: chat không tự lưu khoản mà ngân hàng có thể đã báo (BR-DUP-06) và trả <c>suggestedDuplicates</c>,
/// vẫn giữ <c>possibleDuplicateTransactionId</c> cho app cũ (D3); tạo tay có ứng viên → lưu bình thường kèm nghi trùng (D1); khoản hoàn mồ côi
/// thì không so.
/// </summary>
public class InputDuplicateTests
{
    private static readonly DuplicateSummaryDto BankRow = new(Guid.NewGuid(), 50_000, DateTimeOffset.UtcNow, "Notification", "HIGHLANDS", "Ăn uống", "VCB");

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Mock<IDuplicateDetector> _duplicates = new();

    // ---------------------------------------------------------------- chat

    private readonly Mock<IAIServiceClient> _ai = new();
    private readonly Mock<ICreateManualTransactionCommandHandler> _create = new();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<ITransactionRepository> _transactions = new();
    private DuplicateInput? _asked;

    private ChatCommandHandler Chat()
    {
        _accounts.Setup(a => a.GetListByUserAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinancialAccount> { new() { Id = _accountId, UserId = _userId } });
        _categories.Setup(c => c.GetSystemBySlugAsync("food", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Category { Id = Guid.NewGuid(), Slug = "food" });
        _ai.Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatParseResponse("add_expense", null,
                new ChatParseEntry("expense", 50_000, "trà sữa", null, "food", null, true, false), null, 5));
        return new ChatCommandHandler(
            _ai.Object, _create.Object, _accounts.Object, _categories.Object, new Mock<IReportRepository>().Object, _transactions.Object,
            new ChatCommandValidator(), duplicates: _duplicates.Object);
    }

    private void DetectorSuggests(DuplicateSuggestion suggestion)
        => _duplicates.Setup(d => d.SuggestForInputAsync(It.IsAny<DuplicateInput>(), It.IsAny<CancellationToken>()))
            .Callback<DuplicateInput, CancellationToken>((i, _) => _asked = i)
            .ReturnsAsync(suggestion);

    [Fact]
    public async Task Chat_ABankNotificationMayAlreadyHaveIt_ConfirmsInsteadOfSaving()
    {
        DetectorSuggests(new DuplicateSuggestion([BankRow], MarksSuspected: true));

        var result = await Chat().HandleAsync(new ChatCommand(_userId, "trà sữa 50k", null));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.SuggestedDuplicates.Should().ContainSingle().Which.Id.Should().Be(BankRow.Id);
        result.Draft.PossibleDuplicateTransactionId.Should().Be(BankRow.Id, "app cũ chỉ đọc trường này");
        _create.Verify(c => c.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        _asked!.Source.Should().Be(TransactionSource.Manual);
        _asked.Type.Should().Be(TransactionType.Debit);
        _asked.AccountId.Should().Be(_accountId);
    }

    [Fact]
    public async Task Chat_NoCandidate_StillSavesStraightAway()
    {
        DetectorSuggests(DuplicateSuggestion.None);
        _create.Setup(c => c.HandleAsync(It.IsAny<CreateManualTransactionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionCreationResult(
                new TransactionDto(Guid.NewGuid(), _accountId, null, null, "Ăn uống", 50_000, "Debit", "Manual", "Confirmed", null, null, DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow),
                false));

        var result = await Chat().HandleAsync(new ChatCommand(_userId, "trà sữa 50k", null));

        result.Action.Should().Be(ChatActions.Saved);
    }

    [Fact]
    public async Task Chat_TheTenMinuteGuardStillWins_ForTheOldField()
    {
        var justSaved = Guid.NewGuid();
        _transactions.Setup(t => t.FindPossibleDuplicateAsync(_userId, 50_000, It.IsAny<DateTimeOffset>(), TimeSpan.FromMinutes(10), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(justSaved);
        DetectorSuggests(new DuplicateSuggestion([BankRow], MarksSuspected: true));

        var result = await Chat().HandleAsync(new ChatCommand(_userId, "trà sữa 50k", null));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.PossibleDuplicateTransactionId.Should().Be(justSaved);
        result.Draft.SuggestedDuplicates.Should().ContainSingle();
    }

    // ---------------------------------------------------------------- tạo tay

    private (CreateManualTransactionCommandHandler Handler, List<Transaction> Saved) Manual(
        FinMate.Application.Transactions.Refunds.IRefundLedger? refunds = null)
    {
        var transactions = new Mock<ITransactionRepository>();
        var saved = new List<Transaction>();
        transactions.Setup(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .Callback<Transaction, CancellationToken>((t, _) => saved.Add(t))
            .Returns(Task.CompletedTask);
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(r => r.GetByIdAsync(_accountId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialAccount { Id = _accountId, UserId = _userId, BalanceCents = 1_000_000 });
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Category { Id = id, Slug = "food", Name = "Ăn uống" });
        var budgets = new Mock<IBudgetPeriodService>();
        budgets.Setup(b => b.ApplyDeltaAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new CreateManualTransactionCommandHandler(
            transactions.Object, accounts.Object, categories.Object, budgets.Object, new Mock<IBudgetAlertNotifier>().Object,
            new Mock<IAIServiceClient>().Object, new Mock<IReceiptScanRepository>().Object, new Mock<ICacheService>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateManualTransactionCommandHandler>.Instance,
            new CreateManualTransactionCommandValidator(), refunds: refunds, duplicates: _duplicates.Object);
        return (handler, saved);
    }

    [Fact]
    public async Task Manual_WithACandidate_IsSavedConfirmedAndSuspected()
    {
        var bank = Guid.NewGuid();
        _duplicates.Setup(d => d.MarkSuspectedAsync(It.IsAny<Transaction>(), null, null, It.IsAny<CancellationToken>()))
            .Callback<Transaction, string?, Guid?, CancellationToken>((t, _, _, _) =>
            {
                t.DuplicateOfTransactionId = bank;
                t.DuplicateStatus = DuplicateStatus.Suspected;
            })
            .ReturnsAsync(new DuplicateMatch(null, [new RankedDuplicate(bank, DuplicatePair.ManualNotification)]));
        var (handler, saved) = Manual();

        var result = await handler.HandleAsync(new CreateManualTransactionCommand(
            _userId, _accountId, Guid.NewGuid(), 50_000, TransactionType.Debit, DateTimeOffset.UtcNow, "trà sữa", null));

        var tx = saved.Should().ContainSingle().Subject;
        tx.Status.Should().Be(TransactionStatus.Confirmed);
        tx.DuplicateOfTransactionId.Should().Be(bank);
        tx.DuplicateStatus.Should().Be(DuplicateStatus.Suspected);
        tx.Id.Should().NotBe(Guid.Empty);
        result.Transaction.Id.Should().Be(tx.Id);
    }

    [Fact]
    public async Task Manual_OrphanRefund_IsNeverCompared()
    {
        var refunds = new Mock<FinMate.Application.Transactions.Refunds.IRefundLedger>();
        refunds.Setup(r => r.MarkOrphanAsync(It.IsAny<Guid>(), It.IsAny<Transaction>(), It.IsAny<Category>(), It.IsAny<FinMate.Application.Transactions.Refunds.RefundScopeSnapshot?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinMate.Application.Transactions.Refunds.RefundEffects([], [], []));
        var (handler, _) = Manual(refunds.Object);

        await handler.HandleAsync(new CreateManualTransactionCommand(
            _userId, _accountId, Guid.NewGuid(), 50_000, TransactionType.Credit, DateTimeOffset.UtcNow, null, null, Relation: TransactionRelation.Refund));

        _duplicates.Verify(d => d.MarkSuspectedAsync(It.IsAny<Transaction>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
