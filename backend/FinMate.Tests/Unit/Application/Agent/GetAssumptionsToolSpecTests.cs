using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>
/// Planner chỉ thấy mô tả công cụ. Nếu mô tả <c>get_assumptions</c> không nói khóa nào đang có thì LLM tự đoán tên khóa ("salary_growth"...) và công cụ trả "thiếu giả định"
/// dù giả định đã duyệt. Danh sách khóa lấy từ DB lúc lập kế hoạch, không viết cứng, nên admin thêm khóa mới là planner biết.
/// </summary>
public class GetAssumptionsToolSpecTests
{
    private static GetAssumptionsAgentTool ToolWith(IReadOnlyList<string> keys, bool fails = false)
    {
        var repository = new Mock<IEconomicAssumptionRepository>();
        if (fails)
        {
            repository.Setup(r => r.ListApprovedKeysAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        }
        else
        {
            repository.Setup(r => r.ListApprovedKeysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(keys);
        }

        return new GetAssumptionsAgentTool(new GetAssumptionsTool(repository.Object));
    }

    [Fact]
    public async Task TheRuntimeDescriptionListsTheApprovedKeys()
    {
        var spec = await ToolWith(["general_price_growth", "wage_growth"]).GetSpecAsync();

        spec.Name.Should().Be("get_assumptions");
        spec.Description.Should().Contain("general_price_growth").And.Contain("wage_growth");
        spec.Args.Should().ContainSingle(a => a.Name == "key");
    }

    [Fact]
    public async Task WithNoApprovedKeyTheDescriptionSaysSoInsteadOfInventingOne()
    {
        var spec = await ToolWith([]).GetSpecAsync();

        spec.Description.Should().Contain("chưa có khóa nào").And.NotContain("wage_growth");
    }

    [Fact]
    public async Task TheRegistryUsesTheRuntimeDescriptionForThatToolOnly()
    {
        var registry = new ToolRegistry([ToolWith(["wage_growth"])]);

        var specs = await registry.SpecsAsync();

        specs.Should().ContainSingle(s => s.Name == "get_assumptions" && s.Description.Contains("wage_growth"));
    }

    [Fact]
    public async Task WhenTheDatabaseFailsThePlannerStillGetsTheStaticDescription()
    {
        var tool = ToolWith([], fails: true);
        var registry = new ToolRegistry([tool]);

        var specs = await registry.SpecsAsync();

        specs.Should().ContainSingle(s => s.Name == "get_assumptions" && s.Description == tool.Spec.Description);
    }
}
