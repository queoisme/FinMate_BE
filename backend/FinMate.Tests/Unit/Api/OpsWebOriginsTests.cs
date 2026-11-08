using FinMate.API.Configuration;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Api;

public class OpsWebOriginsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMeansNoBrowserIsAllowed(string? configured)
        => OpsWebOrigins.Parse(configured).Should().BeEmpty();

    [Fact]
    public void ParsesAndDeduplicatesAList()
        => OpsWebOrigins.Parse(" https://ops.finmate.vn , https://OPS.finmate.vn/, http://localhost:3000")
            .Should().Equal("https://ops.finmate.vn", "http://localhost:3000");

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.finmate.vn")]
    [InlineData("ops.finmate.vn")]
    [InlineData("https://ops.finmate.vn/admin")]
    [InlineData("https://ops.finmate.vn?x=1")]
    [InlineData("ftp://ops.finmate.vn")]
    public void RefusesAnythingThatIsNotASingleExactOrigin(string configured)
    {
        var act = () => OpsWebOrigins.Parse(configured);
        act.Should().Throw<InvalidOperationException>();
    }
}
