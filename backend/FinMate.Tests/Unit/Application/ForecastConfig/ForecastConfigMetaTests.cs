using System.Text.Json.Nodes;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.ForecastConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.ForecastConfig;

/// <summary>S6: <c>/meta</c> nói đúng điều validator chấp nhận. Mọi thuộc tính có mặt; biên min/max qua được; vượt biên một đơn vị bị từ chối.</summary>
public class ForecastConfigMetaTests
{
    private static readonly SystemConfigDocumentValidator Validator = new();
    private static readonly IReadOnlyList<ForecastConfigField> Fields = ForecastConfigMeta.Fields();

    public static TheoryData<string> IntegerPaths()
    {
        var data = new TheoryData<string>();
        foreach (var field in ForecastConfigMeta.Fields().Where(f => f.Type == ForecastConfigMeta.IntegerType))
        {
            data.Add(field.Path);
        }

        return data;
    }

    [Fact]
    public void EveryPropertyOfBothSectionsIsDescribed()
    {
        Fields.Select(f => f.Path).Should().BeEquivalentTo(ForecastConfigMeta.Paths());
        Fields.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Description));
        Fields.Where(f => f.Type == ForecastConfigMeta.IntegerType).Should().OnlyContain(f => f.Min != null && f.Max != null && f.Min <= f.Max);
    }

    [Fact]
    public void DefaultsShownAreTheCodeDefaults_AndTheyAreInsideTheRange()
    {
        foreach (var field in Fields.Where(f => f.Type == ForecastConfigMeta.IntegerType))
        {
            var value = field.Default!.GetValue<long>();
            value.Should().BeInRange(field.Min!.Value, field.Max!.Value, field.Path);
        }
    }

    [Theory]
    [MemberData(nameof(IntegerPaths))]
    public void TheMinimumAndMaximumAreAccepted(string path)
    {
        var field = Fields.Single(f => f.Path == path);

        IsValid(path, field.Min!.Value).Should().BeTrue($"{path} = {field.Min} (biên dưới trong /meta) phải hợp lệ");
        IsValid(path, field.Max!.Value).Should().BeTrue($"{path} = {field.Max} (biên trên trong /meta) phải hợp lệ");
    }

    [Theory]
    [MemberData(nameof(IntegerPaths))]
    public void OneBeyondEitherEndIsRefused(string path)
    {
        var field = Fields.Single(f => f.Path == path);

        IsValid(path, field.Min!.Value - 1).Should().BeFalse($"{path} = {field.Min - 1} phải bị từ chối");
        IsValid(path, field.Max!.Value + 1).Should().BeFalse($"{path} = {field.Max + 1} phải bị từ chối");
    }

    /// <summary>Đặt một trường; trường đi cặp (≤ hoặc &lt;) được kéo theo vừa đủ để chỉ còn kiểm khoảng của chính trường này. Các trường khác giữ mặc định.</summary>
    private static bool IsValid(string path, long value)
    {
        var node = ForecastConfigSections.Of(new SystemConfigDocument()).ToNode();
        Set(node, path, value);
        var field = Fields.Single(f => f.Path == path);
        if (field.NotAbove is { } upper && Get(node, upper) < value)
        {
            Set(node, upper, value);
        }

        if (field.Below is { } strictUpper && Get(node, strictUpper) <= value)
        {
            Set(node, strictUpper, value + 1);
        }

        foreach (var lower in Fields.Where(f => f.NotAbove == path && Get(node, f.Path) > value))
        {
            Set(node, lower.Path, value);
        }

        foreach (var lower in Fields.Where(f => f.Below == path && Get(node, f.Path) >= value))
        {
            Set(node, lower.Path, value - 1);
        }

        var sections = ForecastConfigSections.FromNode(node);
        return Validator.Validate(sections.ApplyTo(new SystemConfigDocument())).IsValid;
    }

    private static long Get(JsonObject root, string path)
    {
        var parts = path.Split('.');
        return root[parts[0]]![parts[1]]!.GetValue<long>();
    }

    private static void Set(JsonObject root, string path, long value)
    {
        var parts = path.Split('.');
        root[parts[0]]![parts[1]] = value;
    }
}
