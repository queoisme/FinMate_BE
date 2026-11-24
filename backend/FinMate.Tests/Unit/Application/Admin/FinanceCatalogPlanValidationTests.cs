using System.Text.Json;
using FinMate.Application.Admin.FinanceCatalog;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>
/// Kiểm tra dữ liệu admin nhập cho kế hoạch tháng: bảng phân bổ của phương pháp và các trường kế hoạch của câu khảo sát.
/// Gọi thẳng validator (không qua HTTP) — luồng admin qua API được giữ ở FinanceMethodsControllerTests bằng vài ca hợp lệ/không hợp lệ.
/// </summary>
public class FinanceCatalogPlanValidationTests
{
    private static readonly IReadOnlySet<string> Slugs = new HashSet<string> { "food", "transport", "housing" };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static readonly JsonElement ParamDefs = Json(new object[]
    {
        new { key = "a_pct", type = "percent", @default = 50 },
        new { key = "b_pct", type = "percent", @default = 30 },
        new { key = "minutes_x", type = "minutes", @default = 60 },
    });

    private static IEnumerable<string> FieldsOf(Action<FinanceCatalogValidation> validate)
    {
        var v = new FinanceCatalogValidation();
        validate(v);
        var thrown = ((Action)v.ThrowIfInvalid).Should().Throw<ValidationException>().Which;
        return thrown.Errors.Select(e => e.PropertyName);
    }

    private static object Group(string? key, string kind, params string[] categories) => new { key, label = "Nhóm", kind, categories };

    public static IEnumerable<object[]> InvalidAllocations()
    {
        yield return [new { not = "array" }, "allocation"];
        yield return [new[] { Group("khong_co", "spend", "food") }, "allocation[0].key"];
        yield return [new[] { Group("minutes_x", "spend", "food") }, "allocation[0].key"];
        yield return [new[] { Group("a_pct", "lend", "food") }, "allocation[0].kind"];
        yield return [new[] { Group(null, "save") }, "allocation[0].key"];
        yield return [new[] { Group("a_pct", "save", "food") }, "allocation[0].categories"];
        yield return [new[] { Group("a_pct", "spend", "food"), Group("b_pct", "spend", "food") }, "allocation[1].categories"];
        yield return [new[] { Group(null, "spend", "food"), Group(null, "spend", "transport") }, "allocation"];
        yield return [new[] { Group("a_pct", "spend", "danh_muc_khong_co") }, "allocation"];
        yield return [new[] { new { key = "a_pct", label = new string('x', 41), kind = "spend", categories = new[] { "food" } } }, "allocation[0].label"];
        yield return [Enumerable.Range(0, 9).Select(_ => Group(null, "spend")).ToArray(), "allocation"];
    }

    [Theory]
    [MemberData(nameof(InvalidAllocations))]
    public void InvalidAllocations_AreRefusedWithTheFieldThatIsWrong(object allocation, string expectedField)
        => FieldsOf(v => v.Allocation("allocation", Json(allocation), ParamDefs, Slugs)).Should().Contain(expectedField);

    [Fact]
    public void AValidAllocation_PassesWithoutErrors()
    {
        var v = new FinanceCatalogValidation();
        v.Allocation("allocation", Json(new[] { Group("a_pct", "spend", "food", "housing"), Group(null, "spend", "transport"), Group("b_pct", "save") }), ParamDefs, Slugs);

        ((Action)v.ThrowIfInvalid).Should().NotThrow();
    }

    public static IEnumerable<object?[]> InvalidPlanFields()
    {
        yield return ["slider", null, null, Array.Empty<(int?, int?)>(), "answerType"];
        yield return ["amount", "salary", null, Array.Empty<(int?, int?)>(), "planRole"];
        yield return ["choice", "income", null, Array.Empty<(int?, int?)>(), "answerType"];
        yield return ["choice", "fixed", "housing", Array.Empty<(int?, int?)>(), "answerType"];
        yield return ["choice", "flexible", null, new (int?, int?)[] { (0, 5) }, "categorySlug"];
        yield return ["choice", "flexible", "danh_muc_khong_co", new (int?, int?)[] { (0, 5) }, "categorySlug"];
        yield return ["choice", "flexible", "food", new (int?, int?)[] { (null, 5) }, "options[0].shareMinPct"];
        yield return ["choice", "flexible", "food", new (int?, int?)[] { (20, 10) }, "options[0].shareMaxPct"];
        yield return ["choice", "flexible", "food", new (int?, int?)[] { (101, null) }, "options[0].shareMinPct"];
    }

    [Theory]
    [MemberData(nameof(InvalidPlanFields))]
    public void InvalidSurveyPlanFields_AreRefusedWithTheFieldThatIsWrong(
        string answerType, string? planRole, string? categorySlug, (int?, int?)[] shares, string expectedField)
        => FieldsOf(v => v.SurveyPlanFields(answerType, planRole, categorySlug, shares, Slugs)).Should().Contain(expectedField);

    [Fact]
    public void AFlexibleQuestionWithOpenEndedShares_Passes()
    {
        var v = new FinanceCatalogValidation();
        v.SurveyPlanFields("choice", "flexible", "food", [(0, 5), (5, null)], Slugs);

        ((Action)v.ThrowIfInvalid).Should().NotThrow();
    }
}
