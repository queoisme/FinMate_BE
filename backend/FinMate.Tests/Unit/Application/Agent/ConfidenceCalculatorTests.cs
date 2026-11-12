using FinMate.Application.Agent;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Độ tin cậy = độ đầy đủ × độ mới, bị chặn trên theo loại nguồn. KHÔNG phải xác suất thống kê.</summary>
public class ConfidenceCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void fresh_confirmed_full_completeness_is_1_0()
        => ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now, Now, 1.0).Should().Be(1.0);

    [Fact]
    public void stale_evidence_lowers_confidence()
    {
        var confidence = ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now.AddDays(-40), Now, 1.0);
        confidence.Should().BeLessThanOrEqualTo(0.5);
        confidence.Should().BeApproximately(0.2, 1e-9);
        ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now.AddDays(-40), Now, 0.5)
            .Should().BeLessThanOrEqualTo(0.2 * 0.5 + 1e-9);
    }

    [Fact]
    public void estimated_is_capped_at_0_7()
        => ConfidenceCalculator.Compute(EvidenceSource.Estimated, Now, Now, 1.0).Should().Be(0.7);

    [Fact]
    public void user_entered_is_capped_at_0_9()
        => ConfidenceCalculator.Compute(EvidenceSource.UserEntered, Now, Now, 1.0).Should().Be(0.9);

    [Fact]
    public void completeness_zero_gives_zero()
        => ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now, Now, 0.0).Should().Be(0.0);

    [Fact]
    public void completeness_is_clamped_to_0_1()
    {
        ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now, Now, 7.0).Should().Be(1.0);
        ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now, Now, -3.0).Should().Be(0.0);
    }

    [Fact]
    public void as_of_in_the_future_counts_as_fresh()
        => ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now.AddDays(3), Now, 1.0).Should().Be(1.0);

    [Theory]
    [InlineData(2, 1.0)]
    [InlineData(3, 0.8)]
    [InlineData(7, 0.8)]
    [InlineData(8, 0.5)]
    [InlineData(30, 0.5)]
    [InlineData(31, 0.2)]
    public void freshness_buckets_have_exact_boundaries(int ageDays, double expected)
        => ConfidenceCalculator.Compute(EvidenceSource.Confirmed, Now.AddDays(-ageDays), Now, 1.0)
            .Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void evidence_money_is_a_long_never_a_floating_point()
    {
        var evidence = new Evidence("e1", "balance", 1_500_000L, null, "VND", EvidenceSource.Confirmed, Now, 1.0);
        evidence.ValueVnd.Should().Be(1_500_000L);
        typeof(Evidence).GetProperty(nameof(Evidence.ValueVnd))!.PropertyType.Should().Be(typeof(long?));
    }
}
