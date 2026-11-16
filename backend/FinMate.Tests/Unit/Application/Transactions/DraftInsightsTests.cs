using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

public class DraftInsightsTests
{
    private static ChatParseEntry Entry(string? slug, string? source, long cents = 35_000)
        => new("expense", cents, "cà phê", null, slug, null, true, false, null, source);

    [Theory]
    [InlineData(TransactionSource.Notification, "Notification")]
    [InlineData(TransactionSource.Voice, "Voice")]
    [InlineData(TransactionSource.Receipt, "Receipt")]
    [InlineData(TransactionSource.Manual, "Manual")]
    public void Source_maps_to_channel(TransactionSource source, string channel)
        => DraftInsights.Of(source).Channel.Should().Be(channel);

    [Fact]
    public void Chat_confidence_uses_the_configured_table_and_min_of_known_fields()
    {
        var (text, c) = DraftInsights.ForChat(Entry("food", "memory"), new AssistantSettings());

        c.Category.Should().Be(0.95);
        c.Amount.Should().Be(1.0);
        c.Overall.Should().Be(0.95);
        c.Merchant.Should().BeNull();
        text.Should().Contain("từng chọn");
    }

    [Fact]
    public void Chat_without_a_category_has_no_category_confidence_and_asks_user_to_pick()
    {
        var (text, c) = DraftInsights.ForChat(Entry(null, null), new AssistantSettings());

        c.Category.Should().BeNull();
        c.Overall.Should().Be(1.0);   // chỉ số tiền là có căn cứ
        text.Should().Contain("chọn giúp");
    }

    [Fact]
    public void Unknown_source_is_not_given_a_made_up_number()
    {
        var cfg = new AssistantSettings { CategorySourceConfidence = new Dictionary<string, double>() };

        DraftInsights.ForChat(Entry("food", "keyword"), cfg).Confidence.Category.Should().BeNull();
    }

    [Fact]
    public void Receipt_uses_scan_confidence_and_explains_the_total()
    {
        var dto = new ScannedReceiptDto("success", 120_000, "debit", "Highlands", null, "food", 0.8);

        var (text, c) = DraftInsights.ForReceipt(dto, "brand");

        c.Overall.Should().Be(0.8);
        text.Should().Contain("tổng cộng").And.Contain("thương hiệu");
    }

    [Fact]
    public void Notification_classification_takes_the_weakest_link_and_the_categorizer_version()
    {
        var result = new AiResult { ExtractionConfidence = 0.98, CategorizationConfidence = 0.7, CategorySlug = "food", CategorizerVersion = "1.2.0" };

        var ai = DraftInsights.ForNotification(result)!;

        ai.Confidence.Should().Be(0.7);
        ai.Model.Should().Be("1.2.0");
        DraftInsights.ForNotification(new AiResult()).Should().BeNull();
    }
}
