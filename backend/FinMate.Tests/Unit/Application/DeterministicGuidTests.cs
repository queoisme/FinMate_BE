using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application;

public class DeterministicGuidTests
{
    [Fact]
    public void MatchesTheRfcExample()
    {
        // RFC 9562 phụ lục A.4: namespace DNS + "www.example.com".
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        DeterministicGuid.V5(dns, "www.example.com").Should().Be(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"));
    }

    [Fact]
    public void SameInput_SameId_DifferentName_DifferentId()
    {
        var id = Guid.NewGuid();

        DeterministicGuid.V5(id, "fee").Should().Be(DeterministicGuid.V5(id, "fee"));
        DeterministicGuid.V5(id, "fee").Should().NotBe(DeterministicGuid.V5(id, "other"));
    }
}
