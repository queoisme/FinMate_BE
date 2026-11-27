using FinMate.Backtest.Generation;
using FluentAssertions;

namespace FinMate.Tests.Unit.Backtest;

public class RngTests
{
    [Fact]
    public void SplitMix64_MatchesTheReferenceSequenceForSeedZero()
    {
        // Giá trị tham chiếu của splitmix64.c (Vigna) với state = 0.
        var rng = new SplitMix64(0);

        rng.NextUInt64().Should().Be(0xE220A8397B1DCDAFUL);
        rng.NextUInt64().Should().Be(0x6E789E6AA1B965F4UL);
        rng.NextUInt64().Should().Be(0x06C45D188009454FUL);
    }

    [Fact]
    public void SameStreamKey_GivesTheSameSequence()
    {
        var a = Rng.ForStream("20261127|stable_worker|3|base.flex.food");
        var b = Rng.ForStream("20261127|stable_worker|3|base.flex.food");

        Enumerable.Range(0, 1000).Select(_ => a.LogNormalMoney(50_000, 0.5))
            .Should().Equal(Enumerable.Range(0, 1000).Select(_ => b.LogNormalMoney(50_000, 0.5)));
    }

    [Fact]
    public void DifferentStreams_AreIndependentOfEachOther()
    {
        // Rút từ luồng B nhiều hay ít không đổi được luồng A: thêm một luồng mới không làm xê dịch dữ liệu cũ (đặc tả §2).
        var alone = Rng.ForStream("k|A");
        var expected = Enumerable.Range(0, 50).Select(_ => alone.NextDouble()).ToList();

        var a = Rng.ForStream("k|A");
        var b = Rng.ForStream("k|B");
        var interleaved = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            _ = b.NextDouble();
            _ = b.NextDouble();
            interleaved.Add(a.NextDouble());
        }

        interleaved.Should().Equal(expected);
        Rng.ForStream("k|A").NextDouble().Should().NotBe(Rng.ForStream("k|B").NextDouble());
    }

    [Fact]
    public void Money_IsRoundedToTheStep_AndInsideTheRange()
    {
        var rng = Rng.ForStream("money");
        for (var i = 0; i < 2000; i++)
        {
            var amount = rng.Money(9_000_000, 18_000_000);
            (amount % 100_000).Should().Be(0);
            amount.Should().BeInRange(9_000_000, 18_000_000);

            var small = rng.LogNormalMoney(800, 1.0);
            (small % Rng.MoneyStep).Should().Be(0);
            small.Should().BeGreaterThanOrEqualTo(Rng.MoneyStep);
        }
    }

    [Fact]
    public void Int_CoversBothEnds()
    {
        var rng = Rng.ForStream("int");
        var values = Enumerable.Range(0, 2000).Select(_ => rng.Int(-2, 3)).ToHashSet();

        values.Should().BeEquivalentTo([-2, -1, 0, 1, 2, 3]);
    }

    [Fact]
    public void Poisson_HasTheExpectedMean()
    {
        var rng = Rng.ForStream("poisson");
        var mean = Enumerable.Range(0, 20_000).Select(_ => rng.Poisson(1.6)).Average();

        mean.Should().BeApproximately(1.6, 0.05);
        rng.Poisson(0).Should().Be(0);
    }

    [Fact]
    public void LogNormal_HasTheRequestedMedian()
    {
        var rng = Rng.ForStream("lognormal");
        var values = Enumerable.Range(0, 20_001).Select(_ => rng.LogNormalMoney(100_000, 0.6)).OrderBy(v => v).ToList();

        values[10_000].Should().BeInRange(96_000, 104_000);
    }
}
