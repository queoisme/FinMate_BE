using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Phase 95 (§9): audit của system-config nêu đúng nhóm đã đổi cho MỌI nhóm của tài liệu, kể cả nhóm thêm sau này.</summary>
public class SystemConfigChangedSectionsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Sections()
    {
        var data = new TheoryData<string>();
        foreach (var property in typeof(SystemConfigDocument).GetProperties())
        {
            data.Add(JsonNamingPolicy.CamelCase.ConvertName(property.Name));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public void ChangingOneLeafOfASection_NamesExactlyThatSection(string section)
    {
        var node = JsonSerializer.SerializeToNode(new SystemConfigDocument(), Json)!.AsObject();
        (Mutate(node[section]!, false) || Mutate(node[section]!, true)).Should().BeTrue($"nhóm {section} phải có ít nhất một giá trị để đổi");
        var after = node.Deserialize<SystemConfigDocument>(Json)!;

        SystemConfigService.ChangedSections(new SystemConfigDocument(), after).Should().Equal(section);
    }

    [Fact]
    public void TheFourSectionsTheOldHandWrittenListMissedAreNamed()
    {
        var after = new SystemConfigDocument
        {
            Kpi = new KpiSettings { MinCellSize = 7 },
            DataExport = new DataExportSettings { CsvPerHour = 5 },
            NotificationPipeline = new NotificationPipelineSettings { BusinessDuplicateWindowMinutes = 9 },
        };

        SystemConfigService.ChangedSections(new SystemConfigDocument(), after)
            .Should().BeEquivalentTo("kpi", "dataExport", "notificationPipeline");
    }

    /// <summary>Đổi giá trị lá đầu tiên tìm được (số, chuỗi, bool) theo cách vẫn đọc lại được.</summary>
    private static bool Mutate(JsonNode node, bool allowStrings)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue value && TryChange(value, allowStrings, out var changed))
                    {
                        obj[key] = changed;
                        return true;
                    }

                    if (child is not null && child is not JsonValue && Mutate(child, allowStrings))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && TryChange(value, allowStrings, out var changed))
                    {
                        array[i] = changed;
                        return true;
                    }

                    if (array[i] is { } child && child is not JsonValue && Mutate(child, allowStrings))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryChange(JsonValue value, bool allowStrings, out JsonNode changed)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number:
                changed = value.TryGetValue<long>(out var whole) ? JsonValue.Create(whole + 1) : JsonValue.Create(value.GetValue<double>() + 0.5);
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                changed = JsonValue.Create(!value.GetValue<bool>())!;
                return true;
            case JsonValueKind.String when allowStrings:
                changed = JsonValue.Create(value.GetValue<string>() + "x")!;
                return true;
            default:
                changed = value;
                return false;
        }
    }
}
