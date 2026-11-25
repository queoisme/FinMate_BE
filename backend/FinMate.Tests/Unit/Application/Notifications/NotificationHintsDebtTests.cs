using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Notifications;

/// <summary>Phase 90 (BR-DEBT-12 của Phase 86 cũ, S3): gợi ý trả nợ của AI Service, hai chiều, kèm độ mạnh.</summary>
public class NotificationHintsDebtTests
{
    private static ExtractionResult Extraction(string? hint, string? strength = null)
        => new(500_000, "credit", null, "NAM TRA NO", DateTimeOffset.UtcNow, null, 0.9, hint, null, DebtHintStrength: strength);

    [Theory]
    [InlineData(TransactionType.Credit, "strong", DebtHintStrength.Strong)]
    [InlineData(TransactionType.Debit, "weak", DebtHintStrength.Weak)]
    [InlineData(TransactionType.Credit, null, DebtHintStrength.Weak)]   // AI cũ không gửi độ mạnh: coi là yếu
    [InlineData(TransactionType.Debit, "STRONG", DebtHintStrength.Strong)]
    public void DebtRepayment_IsReadInBothDirections_WithItsStrength(TransactionType type, string? strength, DebtHintStrength expected)
    {
        var (hint, kind, debtStrength) = NotificationHints.Parse(Extraction("debt_repayment", strength), type);

        hint.Should().Be(NotificationRelationHint.DebtRepayment);
        kind.Should().BeNull();
        debtStrength.Should().Be(expected);
    }

    [Fact]
    public void OtherHints_CarryNoStrength_AndTransfersAreNotDebts()
    {
        NotificationHints.Parse(Extraction("refund", "strong"), TransactionType.Credit).DebtStrength.Should().BeNull();
        NotificationHints.Parse(Extraction("debt_repayment"), TransactionType.Transfer).Hint.Should().BeNull();
        NotificationHints.Parse(Extraction("debt_something_new"), TransactionType.Credit).Hint.Should().BeNull();
    }

    [Fact]
    public void TheWireFieldIsSnakeCase()
    {
        var json = """{"amount_cents":500000,"transaction_type":"credit","relation_hint":"debt_repayment","debt_hint_strength":"weak"}""";

        var parsed = JsonSerializer.Deserialize<ExtractionApiResult>(json)!;

        parsed.RelationHint.Should().Be("debt_repayment");
        parsed.DebtHintStrength.Should().Be("weak");
    }
}
