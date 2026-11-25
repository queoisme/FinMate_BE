using FinMate.Application.Loans;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

/// <summary>Xếp hạng gợi ý khoản vay (Phase 90, BR-DEBT-13, D8, S1, S3, Q15). Chép từ bản sổ nợ Phase 86 cũ, thêm phần lãi và luật dấu theo câu.</summary>
public class LoanSuggestionRankingTests
{
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static readonly string[] Honorifics = ["bạn", "anh", "chị", "em", "cô", "chú", "bác", "ông", "bà", "dì", "cậu"];

    private static Loan Loan(
        string name, LoanDirection direction = LoanDirection.Lend, long remaining = 1_500_000, DateOnly? due = null, int startDaysAfter = 0, decimal rate = 0)
        => new()
        {
            Id = Guid.NewGuid(), Title = $"Khoản {name}", Counterparty = name, Direction = direction, Status = LoanStatus.Open, PrincipalCents = remaining,
            RepaidCents = 0, DueDate = due, StartDate = new DateOnly(2026, 9, 1).AddDays(startDaysAfter), InterestRatePercentYear = rate,
        };

    private static IReadOnlyList<LoanSuggestionDto> Rank(LoanSuggestionInput input, int max, params Loan[] loans)
        => LoanSuggestionRanker.Rank(input, loans, new LoanSuggestionRules(Today, 10_000, Honorifics, max));

    private static IReadOnlyList<LoanSuggestionDto> Rank(LoanSuggestionInput input, params Loan[] loans) => Rank(input, 3, loans);

    [Fact]
    public void ANameInTheText_WinsOverEverythingElse()
    {
        var nam = Loan("Nam", remaining: 2_000_000);
        var lan = Loan("Lan", remaining: 500_000, due: new DateOnly(2026, 12, 16));

        var result = Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "+500,000 NAM TRA NO", DebtHintStrength.Strong), lan, nam);

        result.Select(r => r.Counterparty).Should().Equal("Nam", "Lan");
        (result[0].NameMatched, result[1].NameMatched).Should().Be((true, false));
    }

    [Fact]
    public void WithAStrongHintButNoName_PayingItOffThenNearestDueThenOldest()
    {
        var exact = Loan("A", remaining: 500_000, startDaysAfter: 5);
        var soon = Loan("B", remaining: 900_000, due: new DateOnly(2026, 12, 20));
        var later = Loan("C", remaining: 900_000, due: new DateOnly(2027, 1, 20));
        var undated = Loan("D", remaining: 900_000);

        var result = Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "CK TRA NO", DebtHintStrength.Strong), 10, undated, later, soon, exact);

        result.Select(r => r.Counterparty).Should().Equal("A", "B", "C", "D");
    }

    [Fact]
    public void AWeakHintOrNoHint_SuggestsOnlyWhenTheNameMatches()
    {
        var nam = Loan("Nam");

        Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "Shopee tra lai tien don hang", DebtHintStrength.Weak), nam).Should().BeEmpty();
        Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "luong thang 11", null), nam).Should().BeEmpty();
        Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "NAM GUI TRA", DebtHintStrength.Weak), nam).Should().ContainSingle();
        Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "Nam chuyen tien", null), nam).Should().ContainSingle();
    }

    [Fact]
    public void OnlyTheRepaymentDirection_AndOnlyUpToTheRemaining_WithoutInterest()
    {
        var lent = Loan("Nam", LoanDirection.Lend, remaining: 1_000_000);
        var borrowed = Loan("Nam", LoanDirection.Borrow, remaining: 1_000_000);

        Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "NAM", null), lent, borrowed).Single().Direction.Should().Be("lend");
        Rank(new LoanSuggestionInput(TransactionType.Debit, 500_000, "NAM", null), lent, borrowed).Single().Direction.Should().Be("borrow");
        Rank(new LoanSuggestionInput(TransactionType.Credit, 1_000_001, "NAM", DebtHintStrength.Strong), lent).Should().BeEmpty();
        Rank(new LoanSuggestionInput(TransactionType.Transfer, 500_000, "NAM", DebtHintStrength.Strong), lent).Should().BeEmpty();
    }

    [Fact]
    public void S1_AnAmountOverTheRemainingWithinTheInterest_IsACandidate_WithItsInterestPart()
    {
        var withRate = Loan("Nam", remaining: 2_000_000, rate: 12);   // 1/9 → 30/11: 90 ngày, lãi 59.178

        var result = Rank(new LoanSuggestionInput(TransactionType.Credit, 2_060_000, "NAM TRA NO", null), withRate);

        result.Single().InterestCents.Should().Be(60_000);
        Rank(new LoanSuggestionInput(TransactionType.Credit, 2_080_000, "NAM TRA NO", null), withRate).Should().BeEmpty("vượt lãi tạm tính + ngưỡng");
    }

    [Fact]
    public void ClosedOrDeletedLoans_AreNotCandidates()
    {
        var settled = Loan("Nam");
        settled.Status = LoanStatus.Settled;
        var writtenOff = Loan("Nam");
        writtenOff.Status = LoanStatus.WrittenOff;
        var deleted = Loan("Nam");
        deleted.DeletedAt = DateTimeOffset.UtcNow;

        Rank(new LoanSuggestionInput(TransactionType.Credit, 1, "NAM", DebtHintStrength.Strong), settled, writtenOff, deleted).Should().BeEmpty();
    }

    /// <summary>Q15: "HOA TRA NO" không dấu khớp cả khoản "Hoa" lẫn "Hòa"; cả hai được trả, xếp hạng như thường.</summary>
    [Fact]
    public void Q15_AnUnaccentedNotification_ReturnsBothHoaAndHoa()
    {
        var hoa = Loan("Hoa", remaining: 800_000, due: new DateOnly(2027, 1, 10));
        var hoaAccented = Loan("Hòa", remaining: 800_000, due: new DateOnly(2026, 12, 10));

        var result = Rank(new LoanSuggestionInput(TransactionType.Credit, 500_000, "HOA TRA NO", null), hoa, hoaAccented);

        result.Select(r => r.Counterparty).Should().Equal("Hòa", "Hoa");
        result.Should().OnlyContain(r => r.NameMatched);
    }

    [Fact]
    public void AtMostMax()
    {
        var loans = Enumerable.Range(0, 5).Select(i => Loan($"Người {i}")).ToArray();

        Rank(new LoanSuggestionInput(TransactionType.Credit, 1, "tra no", DebtHintStrength.Strong), 3, loans).Should().HaveCount(3);
    }
}
