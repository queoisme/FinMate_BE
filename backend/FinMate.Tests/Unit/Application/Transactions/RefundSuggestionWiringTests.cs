using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Notifications;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>Phase 81, Task 11: push <c>choose_refund_link</c> (D16), cấu hình <c>refunds.*</c>, gợi ý trong chat (D17).</summary>
public class RefundSuggestionWiringTests
{
    private static readonly Transaction Draft = new()
    {
        Id = Guid.NewGuid(), AmountCents = 900_000, TransactionType = TransactionType.Credit, MerchantName = "Shopee", Status = TransactionStatus.Draft,
    };

    [Fact]
    public void RefundHint_NeverOneTap_EvenAtFullConfidence()
    {
        var push = TransactionReviewPushBuilder.Build(Draft, "Thu nhập", 1.0, TransactionRelation.Refund, 2);

        push.Data["action"].Should().Be(TransactionReviewPushBuilder.ActionChooseRefundLink);
        push.Data["relationHint"].Should().Be("Refund");
        push.Data["suggestionCount"].Should().Be("2");
        push.Body.Should().Contain("900.000đ");
    }

    [Fact]
    public void WithoutHint_TheOldBranchesStay()
        => TransactionReviewPushBuilder.Build(Draft, "Thu nhập", 0.95).Data["action"].Should().Be(TransactionReviewPushBuilder.ActionOneTap);

    [Theory]
    [InlineData("refund", TransactionRelation.Refund)]
    [InlineData("REVERSAL", TransactionRelation.Reversal)]
    [InlineData("cashback", null)]
    [InlineData(null, null)]
    public void HintParsing_IgnoresUnknownValues(string? raw, TransactionRelation? expected)
        => IRefundSuggestions.Parse(raw).Should().Be(expected);

    [Theory]
    [InlineData(60, 3, 3, true)]
    [InlineData(0, 3, 3, false)]
    [InlineData(400, 3, 3, false)]
    [InlineData(60, 61, 3, false)]
    [InlineData(60, 3, 0, false)]
    public void RefundSettings_AreValidated(int window, int reversalWindow, int max, bool valid)
        => new SystemConfigDocumentValidator()
            .Validate(new SystemConfigDocument { Refunds = new RefundSettings { MatchWindowDays = window, ReversalMatchWindowDays = reversalWindow, MaxSuggestions = max } })
            .IsValid.Should().Be(valid);

    [Fact]
    public async Task ChatRefund_ComesBackAsADraftWithSuggestions_NeverSaved()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var ai = new Mock<IAIServiceClient>();
        ai.Setup(a => a.ParseChatAsync(It.IsAny<ChatParseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatParseResponse("add_income", null, new ChatParseEntry("income", 300_000, "hoàn tiền", "Shopee", "income", "refund", true, false), null, 5));
        var accounts = new Mock<IFinancialAccountRepository>();
        accounts.Setup(a => a.GetListByUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync([new FinancialAccount { Id = accountId, UserId = userId }]);
        var create = new Mock<ICreateManualTransactionCommandHandler>();
        var suggestion = new RefundSuggestionDto(Guid.NewGuid(), 900_000, 0, 900_000, "Shopee", null, null, accountId, DateTimeOffset.UtcNow);
        var suggestions = new Mock<IRefundSuggestions>();
        suggestions.Setup(x => x.SuggestAsync(userId, It.Is<Transaction>(t => t.AmountCents == 300_000 && t.TransactionType == TransactionType.Credit), TransactionRelation.Refund, It.IsAny<CancellationToken>()))
            .ReturnsAsync([suggestion]);
        var handler = new ChatCommandHandler(
            ai.Object, create.Object, accounts.Object, Mock.Of<ICategoryRepository>(), Mock.Of<IReportRepository>(), Mock.Of<ITransactionRepository>(),
            new ChatCommandValidator(), refundSuggestions: suggestions.Object);

        var result = await handler.HandleAsync(new ChatCommand(userId, "được hoàn tiền 300k", null));

        result.Action.Should().Be(ChatActions.Confirm);
        result.Draft!.RelationHint.Should().Be("Refund");
        result.Draft.SuggestedRelatedTransactions.Should().ContainSingle().Which.TransactionId.Should().Be(suggestion.TransactionId);
        create.VerifyNoOtherCalls();
    }
}
