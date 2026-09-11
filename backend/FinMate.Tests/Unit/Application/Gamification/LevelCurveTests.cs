using FinMate.Application.Gamification;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Gamification;

public class LevelCurveTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 100)]
    [InlineData(3, 300)]
    [InlineData(4, 600)]
    [InlineData(5, 1000)]
    public void TotalExpForLevel_FollowsTheQuadraticCurve(int level, int expected)
        => LevelCurve.TotalExpForLevel(level).Should().Be(expected);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]   // đúng mốc phải lên level, không được lùi vì sai số dấu phẩy động
    [InlineData(101, 2)]
    [InlineData(299, 2)]
    [InlineData(300, 3)]
    [InlineData(999, 4)]
    [InlineData(1000, 5)]
    public void LevelForExp_IsExactAtEveryThreshold(int exp, int expectedLevel)
        => LevelCurve.LevelForExp(exp).Should().Be(expectedLevel);

    [Fact]
    public void LevelForExp_NegativeExp_StaysAtLevelOne()
        => LevelCurve.LevelForExp(-50).Should().Be(1);

    [Fact]
    public void LevelForExp_HugeExp_IsCappedRatherThanLoopingForever()
        => LevelCurve.LevelForExp(int.MaxValue).Should().Be(LevelCurve.MaxLevel);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(40, 60)]
    [InlineData(100, 200)]
    public void ExpToNextLevel_CountsTheGapToTheNextThreshold(int exp, int expected)
        => LevelCurve.ExpToNextLevel(exp).Should().Be(expected);
}
