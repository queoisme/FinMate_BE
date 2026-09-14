using FinMate.Application.Notifications;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Notifications;

/// <summary>
/// Docx Flow 1 bước 5.2/5.3: cùng một giao dịch nháp, hai cách hỏi khác nhau tuỳ mức tin cậy.
/// </summary>
public class TransactionReviewPushBuilderTests
{
    private static Transaction Draft(
        long amountCents = 75_000,
        TransactionType type = TransactionType.Debit,
        string? merchant = "Highlands Coffee",
        string? categorySlug = "food") => new()
    {
        Id = Guid.NewGuid(),
        AmountCents = amountCents,
        TransactionType = type,
        MerchantName = merchant,
        Category = categorySlug is null
            ? null
            : new Category { Id = Guid.NewGuid(), Slug = categorySlug, Name = "Ăn uống" },
    };

    // ------------------------------------------------------------ chọn nhánh

    [Fact]
    public void TheWeakestLinkDecides()
    {
        // Một chạm chốt TOÀN BỘ bản ghi. Bóc số 0,70 kèm phân loại 0,95 mà cho một chạm là
        // chốt nhanh một con số TIỀN có thể sai — phân loại chắc chắn không cứu được điều đó.
        TransactionReviewPushBuilder.ReviewConfidence(0.70, 0.95).Should().Be(0.70);
        TransactionReviewPushBuilder.ReviewConfidence(0.98, 0.80).Should().Be(0.80);
    }

    [Theory]
    [InlineData(null, 0.95)]
    [InlineData(0.98, null)]
    [InlineData(null, null)]
    public void MissingConfidenceIsNeverTreatedAsCertain(double? extraction, double? categorization)
    {
        var confidence = TransactionReviewPushBuilder.ReviewConfidence(extraction, categorization);

        confidence.Should().BeNull();

        TransactionReviewPushBuilder.Build(Draft(), "Ăn uống", confidence)
            .Data["action"].Should().Be(TransactionReviewPushBuilder.ActionChooseCategory);
    }

    [Theory]
    [InlineData(0.85, TransactionReviewPushBuilder.ActionOneTap)]
    [InlineData(0.90, TransactionReviewPushBuilder.ActionOneTap)]
    [InlineData(0.8499, TransactionReviewPushBuilder.ActionChooseCategory)]
    [InlineData(0.35, TransactionReviewPushBuilder.ActionChooseCategory)]
    public void TheThresholdIsInclusive(double confidence, string expectedAction)
    {
        TransactionReviewPushBuilder.Build(Draft(), "Ăn uống", confidence)
            .Data["action"].Should().Be(expectedAction);
    }

    // ------------------------------------------------------------- nội dung

    [Fact]
    public void TheOneTapBodyIsTheSentenceFromTheDocument()
    {
        var push = TransactionReviewPushBuilder.Build(Draft(), "Ăn uống", 0.92);

        push.Body.Should().Be("Bạn vừa chi 75.000đ tại Highlands Coffee (Ăn uống)?");
    }

    [Fact]
    public void TheUncertainBodyDoesNotNameACategory()
    {
        // Đọc lên một danh mục như thể đã chốt sẽ khiến người dùng gật theo thay vì chọn lại
        // — mà chọn lại mới đúng là việc cần ở nhánh này.
        var push = TransactionReviewPushBuilder.Build(Draft(), "Ăn uống", 0.40);

        push.Body.Should().NotContain("Ăn uống");
        push.Body.Should().Contain("75.000đ").And.Contain("Highlands Coffee");
    }

    [Fact]
    public void MoneyComingInReadsAsReceivedNotSpent()
    {
        var push = TransactionReviewPushBuilder.Build(
            Draft(amountCents: 15_000_000, type: TransactionType.Credit, merchant: "Công ty ABC",
                  categorySlug: "income"),
            "Thu nhập", 0.95);

        push.Body.Should().StartWith("Bạn vừa nhận 15.000.000đ");
    }

    [Fact]
    public void AMissingMerchantDoesNotLeaveADanglingPreposition()
    {
        var push = TransactionReviewPushBuilder.Build(Draft(merchant: null), "Ăn uống", 0.92);

        push.Body.Should().Be("Bạn vừa chi 75.000đ (Ăn uống)?");
        push.Data.Should().NotContainKey("merchantName");
    }

    // --------------------------------------------------------------- payload

    [Fact]
    public void ThePayloadCarriesTheTransactionToActOn()
    {
        // Không có id thì client biết CÓ giao dịch mới nhưng không xác nhận được cái nào —
        // tức là nút một chạm không dựng được, và cả tính năng vô nghĩa.
        var draft = Draft();

        var push = TransactionReviewPushBuilder.Build(draft, "Ăn uống", 0.92);

        push.Data["transactionId"].Should().Be(draft.Id.ToString());
        push.Data["type"].Should().Be("transaction_review");
        push.Data["amountCents"].Should().Be("75000");
        push.Data["categorySlug"].Should().Be("food");
    }

    [Fact]
    public void ADraftTheAiCouldNotCategoriseStillGetsAPayload()
    {
        var push = TransactionReviewPushBuilder.Build(Draft(categorySlug: null), null, 0.40);

        push.Data.Should().NotContainKey("categorySlug");
        push.Data["action"].Should().Be(TransactionReviewPushBuilder.ActionChooseCategory);
    }
}
