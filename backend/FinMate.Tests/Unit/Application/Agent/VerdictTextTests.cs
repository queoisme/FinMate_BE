using FluentAssertions;
using Xunit;
using FinMate.Application.Agent;

namespace FinMate.Tests.Unit.Application.Agent;

public class VerdictTextTests
{
    [Theory]
    [InlineData("verdict: infeasible (mục tiêu trong 36 tháng)", "Kết luận: Khó đạt (mục tiêu trong 36 tháng)")]
    [InlineData("verdict: tight (horizon 24 tháng)", "Kết luận: Cần cân nhắc (mốc 24 tháng)")]
    [InlineData("verdict: infeasible (giảm 30% trong 12 tháng, nhóm thu nhập stable; hết tiền sau khoảng 8 tháng)", "Kết luận: Khó đạt (giảm 30% trong 12 tháng; hết tiền sau khoảng 8 tháng)")]
    [InlineData("verdict: feasible", "Kết luận: Khả thi")]
    public void TheMachineStringBecomesAVietnameseSentenceWithoutInternalLabels(string raw, string expected)
        => VerdictText.Display(raw).Should().Be(expected);

    [Fact]
    public void TextThatIsNotAVerdictIsLeftAlone() => VerdictText.Display("Tổng số dư các tài khoản").Should().Be("Tổng số dư các tài khoản");

    [Fact]
    public void TheEvidenceListShownToTheUserCarriesTheDisplayTextButTheVerdictCodeStillParses()
    {
        var verdict = new Evidence("verdict", "goal_verdict", null, "verdict: tight (horizon 24 tháng)", "label", EvidenceSource.Estimated, DateTimeOffset.UtcNow, 0.5);

        AgentEvidenceDto.From(verdict).Text.Should().Be("Kết luận: Cần cân nhắc (mốc 24 tháng)");
        LocalTemplateComposer.Compose([verdict]).Single().Text.Should().StartWith("Kết luận: Cần cân nhắc");
    }
}
