using System.Text.Json;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;
using static FinMate.Tests.Unit.Application.FinanceMethods.FinanceTestData;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>Logic thuần của khảo sát và bộ chọn: gộp signals, điều kiện hiển thị, shortlist, chọn bằng rule, kiểm đầu ra của AI, tham số hiệu lực.</summary>
public class FinanceMethodLogicTests
{
    // ---- SurveySignals ----

    [Fact]
    public void Aggregate_SumsWeightsPerSignal_AndCapsThemAtTheAiContractMaximum()
    {
        var total = SurveySignals.Aggregate(["""{"a":3,"b":1}""", """{"a":2}""", """{"b":60}"""]);

        total["a"].Should().Be(5);
        total["b"].Should().Be(SurveySignals.MaxWeight, "hợp đồng với ai-service cho weight tối đa 50");
    }

    [Fact]
    public void Aggregate_IgnoresNonPositiveWeights_NonIntegers_AndBrokenJson()
    {
        var total = SurveySignals.Aggregate(["""{"a":0,"b":-2,"c":1.5,"d":"x","e":2}""", "not json", "[]", ""]);

        total.Should().Equal(new Dictionary<string, int> { ["e"] = 2 });
    }

    // ---- ShowIfEvaluator ----

    private const string GoalIsSavingAndNotInTrouble =
        """{"all":[{"question":"goal","in":["B","D"]},{"question":"month_end","not_in":["E"]}]}""";

    [Fact]
    public void ShowIf_NullOrBlank_AlwaysShowsTheQuestion()
    {
        ShowIfEvaluator.IsVisible(null, new Dictionary<string, string>()).Should().BeTrue();
        ShowIfEvaluator.IsVisible("  ", new Dictionary<string, string>()).Should().BeTrue();
    }

    [Theory]
    [InlineData("B", "A", true)]    // mục tiêu tiết kiệm, cuối tháng ổn
    [InlineData("D", "D", true)]    // thỉnh thoảng thiếu vẫn được hỏi (chỉ E mới ẩn)
    [InlineData("B", "E", false)]   // thường xuyên thiếu: ẩn câu cách để dành
    [InlineData("A", "A", false)]   // mục tiêu không phải tiết kiệm: ẩn
    public void ShowIf_AllConditionsMustHold(string goal, string monthEnd, bool expected)
    {
        var answers = new Dictionary<string, string> { ["goal"] = goal, ["month_end"] = monthEnd };

        ShowIfEvaluator.IsVisible(GoalIsSavingAndNotInTrouble, answers).Should().Be(expected);
    }

    [Fact]
    public void ShowIf_AnUnansweredQuestionIsInNoList_SoInFailsAndNotInPasses()
    {
        ShowIfEvaluator.IsVisible("""{"all":[{"question":"goal","in":["B"]}]}""", new Dictionary<string, string>()).Should().BeFalse();
        ShowIfEvaluator.IsVisible("""{"all":[{"question":"goal","not_in":["B"]}]}""", new Dictionary<string, string>()).Should().BeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"all":"x"}""")]
    [InlineData("""{"all":[{"nope":1}]}""")]
    public void ShowIf_BrokenConfiguration_ShowsTheQuestionRatherThanLosingIt(string json)
        => ShowIfEvaluator.IsVisible(json, new Dictionary<string, string>()).Should().BeTrue();

    // ---- MethodSelector ----

    [Fact]
    public void Shortlist_DropsInactiveAndExcludedMethods_AndOrdersByDisplayOrder()
    {
        var methods = new[]
        {
            Method("late", sortOrder: 3),
            Method("off", isActive: false, sortOrder: 1),
            Method("safe_only", excludes: ["cash_shortfall"], sortOrder: 2),
            Method("first", sortOrder: 0),
        };

        MethodSelector.Shortlist(methods, Signals(("cash_shortfall", 3))).Select(m => m.Code).Should().Equal("first", "late");
        MethodSelector.Shortlist(methods, Signals(("other", 1))).Select(m => m.Code).Should().Equal("first", "safe_only", "late");
    }

    [Fact]
    public void Score_IsTheSumOfMethodWeightTimesUserWeight_OverSharedSignalsOnly()
    {
        var method = Method("m", weights: new() { ["a"] = 3, ["b"] = 2, ["unused"] = 9 });

        MethodSelector.Score(method, Signals(("a", 2), ("b", 5), ("other", 7))).Should().Be((3 * 2) + (2 * 5));
    }

    [Fact]
    public void SelectByRule_PicksTheHighestScoringPrimary_AndTiesGoToTheEarlierDisplayOrder()
    {
        var shortlist = new[]
        {
            Method("b", weights: new() { ["x"] = 2 }, sortOrder: 2),
            Method("a", weights: new() { ["x"] = 2 }, sortOrder: 1),
            Method("low", weights: new() { ["x"] = 1 }, sortOrder: 0),
        };

        MethodSelector.SelectByRule(shortlist, Signals(("x", 3)))!.Primary.Code.Should().Be("a");
    }

    [Fact]
    public void SelectByRule_ASupportingOnlyMethodNeverBecomesThePrimary_EvenWhenItScoresHighest()
    {
        var shortlist = new[]
        {
            Method("tool", FinanceMethodRole.Supporting, new() { ["x"] = 10 }),
            Method("main", FinanceMethodRole.Primary, new() { ["x"] = 1 }),
        };

        var selection = MethodSelector.SelectByRule(shortlist, Signals(("x", 3)))!;

        selection.Primary.Code.Should().Be("main");
        selection.Support?.Code.Should().Be("tool");
    }

    [Fact]
    public void SelectByRule_WithNoPrimaryCapableMethod_ReturnsNull()
        => MethodSelector.SelectByRule([Method("tool", FinanceMethodRole.Supporting)], Signals(("x", 1))).Should().BeNull();

    [Fact]
    public void SelectByRule_SupportNeedsAtLeastHalfThePrimaryScore_AndAScoreAboveZero()
    {
        var shortlist = new[]
        {
            Method("main", weights: new() { ["x"] = 10 }),
            Method("weak_tool", FinanceMethodRole.Supporting, new() { ["x"] = 4 }),     // 12 < 15
            Method("no_match_tool", FinanceMethodRole.Supporting),                       // 0
        };

        MethodSelector.SelectByRule(shortlist, Signals(("x", 3)))!.Support.Should().BeNull();

        var withGoodTool = shortlist.Append(Method("good_tool", FinanceMethodRole.Supporting, new() { ["x"] = 5 })).ToList();   // 15 ≥ 15

        MethodSelector.SelectByRule(withGoodTool, Signals(("x", 3)))!.Support!.Code.Should().Be("good_tool");
    }

    [Fact]
    public void SelectByRule_ABothRoleMethodCanBeSupportButNeverTheSameAsThePrimary()
    {
        var shortlist = new[]
        {
            Method("main", FinanceMethodRole.Both, new() { ["x"] = 10 }),
            Method("also_both", FinanceMethodRole.Both, new() { ["x"] = 8 }),
        };

        var selection = MethodSelector.SelectByRule(shortlist, Signals(("x", 3)))!;

        selection.Primary.Code.Should().Be("main");
        selection.Support!.Code.Should().Be("also_both");
    }

    [Fact]
    public void SelectByRule_UsesTheMethodsWhyTextAsTheReason()
    {
        var selection = MethodSelector.SelectByRule([Method("main")], Signals(("x", 1)))!;

        selection.PrimaryReason.Should().Be("Why main");
    }

    // ---- MethodSelector.FromAiPick: lớp kiểm thứ hai ở backend ----

    private static readonly FinMate.Domain.Entities.FinanceMethod[] AiShortlist =
    [
        Method("main", FinanceMethodRole.Primary),
        Method("tool", FinanceMethodRole.Supporting),
        Method("both", FinanceMethodRole.Both),
    ];

    [Fact]
    public void FromAiPick_AValidPick_IsAcceptedAndReasonsAreTrimmed()
    {
        var selection = MethodSelector.FromAiPick("main", "  Lý do một. ", "tool", "Lý do hai.", AiShortlist);

        selection!.Primary.Code.Should().Be("main");
        selection.PrimaryReason.Should().Be("Lý do một.");
        selection.Support!.Code.Should().Be("tool");
    }

    [Fact]
    public void FromAiPick_NoSupport_IsValid()
        => MethodSelector.FromAiPick("main", "ok", null, null, AiShortlist)!.Support.Should().BeNull();

    [Theory]
    [InlineData("ghost", "ok", null, null)]            // mã ngoài shortlist: AI không được đề xuất ngoài danh sách
    [InlineData("tool", "ok", null, null)]             // cách chỉ-bổ-trợ làm cách chính
    [InlineData("main", "ok", "main", "ok")]           // bổ trợ trùng chính
    [InlineData("main", "ok", "ghost", "ok")]          // bổ trợ ngoài danh sách
    [InlineData("main", "ok", "main2", "ok")]
    [InlineData("main", "ok", "tool", null)]           // có bổ trợ mà thiếu lý do
    [InlineData("main", "", null, null)]               // lý do rỗng
    [InlineData("main", "   ", null, null)]
    public void FromAiPick_RejectsAnythingOutsideTheAllowedList_OrWrongRole(
        string primary, string primaryReason, string? support, string? supportReason)
        => MethodSelector.FromAiPick(primary, primaryReason, support, supportReason, AiShortlist).Should().BeNull();

    [Fact]
    public void FromAiPick_RejectsReasonsThatAreTooLongOrMultiLine_WithoutTruncatingThem()
    {
        MethodSelector.FromAiPick("main", new string('x', MethodSelector.MaxReasonLength + 1), null, null, AiShortlist).Should().BeNull();
        MethodSelector.FromAiPick("main", new string('x', MethodSelector.MaxReasonLength), null, null, AiShortlist).Should().NotBeNull();
        MethodSelector.FromAiPick("main", "dòng một\ndòng hai", null, null, AiShortlist).Should().BeNull();
    }

    [Fact]
    public void FromAiPick_ABothRoleMethodMayBePrimaryOrSupport()
    {
        MethodSelector.FromAiPick("both", "ok", null, null, AiShortlist).Should().NotBeNull();
        MethodSelector.FromAiPick("main", "ok", "both", "ok", AiShortlist).Should().NotBeNull();
    }

    // ---- MethodParams ----

    private static readonly string Defs = AlertParams();

    [Fact]
    public void Effective_UsesAValidOverride_ElseTheMethodDefault()
    {
        var defs = MethodParams.ParseDefs(Defs);

        var effective = MethodParams.Effective(defs, new Dictionary<string, long> { ["t1_pct"] = 55, ["t2_pct"] = 100, ["other"] = 3 });

        effective["t1_pct"].Should().Be(55);
        effective["t2_pct"].Should().Be(90, "100 vượt max (99) nên bị bỏ");
        effective["other"].Should().Be(5, "tham số không cho người dùng sửa thì ghi đè bị bỏ");
    }

    [Fact]
    public void ParseDefs_SkipsMalformedEntries_AndNeverThrows()
    {
        MethodParams.ParseDefs("not json").Should().BeEmpty();
        MethodParams.ParseDefs("{}").Should().BeEmpty();
        MethodParams.ParseDefs("""[{"key":"ok","default":1,"min":0,"max":2},{"key":"bad"},5]""").Select(d => d.Key).Should().Equal("ok");
    }

    [Fact]
    public void ParseOverrides_KeepsOnlyIntegerNumbers()
        => MethodParams.ParseOverrides("""{"a":5,"b":"x","c":1.5}""").Should().Equal(new Dictionary<string, long> { ["a"] = 5 });

    [Fact]
    public void ParseOverrides_OfBrokenJson_IsEmpty()
        => MethodParams.ParseOverrides("[").Should().BeEmpty();

    [Fact]
    public void FinanceJson_IsReadByTheMethodEligibilityParser()
    {
        var eligibility = MethodEligibility.Parse(JsonSerializer.Serialize(new { weights = new { a = 2 }, excludes = new[] { "x", "y" } }));

        eligibility.Weights["a"].Should().Be(2);
        eligibility.Excludes.Should().BeEquivalentTo("x", "y");
        MethodEligibility.Parse("garbage").Excludes.Should().BeEmpty();
    }
}
