using System.Text.Json.Nodes;

namespace FinMate.Application.ForecastConfig;

/// <summary>Một trường lá khác nhau giữa hai bản. Mảng (<c>holidays</c>) là một mục, so cả mảng.</summary>
public sealed record ForecastConfigChange(string Path, JsonNode? Before, JsonNode? After);

/// <summary>S2: trường mà cả bản đang chạy lẫn bản nháp đều đã đổi so với bản gốc khi "làm lại". Giá trị bản nháp thắng; chỉ để người duyệt thấy.</summary>
public sealed record ForecastConfigConflict(string Path, JsonNode? BaseValue, JsonNode? ActiveValue, JsonNode? DraftValue);

public static class ForecastConfigDiff
{
    /// <summary>Các trường lá khác nhau, theo thứ tự khai báo.</summary>
    public static IReadOnlyList<ForecastConfigChange> Compute(ForecastConfigSections before, ForecastConfigSections after)
    {
        var a = Leaves(before.ToNode());
        var b = Leaves(after.ToNode());
        return a.Keys.Where(path => !JsonNode.DeepEquals(a[path], b.GetValueOrDefault(path)))
            .Select(path => new ForecastConfigChange(path, a[path]?.DeepClone(), b.GetValueOrDefault(path)?.DeepClone()))
            .ToList();
    }

    /// <summary>
    /// D9 + S2: lấy phần <paramref name="source"/> đã đổi so với <paramref name="sourceBase"/> và áp lên <paramref name="active"/>. Trường mà bản đang chạy cũng đã đổi
    /// so với <paramref name="sourceBase"/> thì vẫn lấy giá trị của bản nháp, nhưng được ghi vào <c>Conflicts</c>.
    /// </summary>
    public static (ForecastConfigSections Sections, IReadOnlyList<ForecastConfigConflict> Conflicts) Rebase(
        ForecastConfigSections source, ForecastConfigSections sourceBase, ForecastConfigSections active)
    {
        var changes = Compute(sourceBase, source);
        var baseLeaves = Leaves(sourceBase.ToNode());
        var target = active.ToNode();
        var targetLeaves = Leaves(target);
        var conflicts = new List<ForecastConfigConflict>();
        foreach (var change in changes)
        {
            var activeValue = targetLeaves.GetValueOrDefault(change.Path);
            if (!JsonNode.DeepEquals(activeValue, baseLeaves.GetValueOrDefault(change.Path)))
            {
                conflicts.Add(new ForecastConfigConflict(change.Path, baseLeaves.GetValueOrDefault(change.Path)?.DeepClone(), activeValue?.DeepClone(), change.After?.DeepClone()));
            }

            Set(target, change.Path, change.After?.DeepClone());
        }

        return (ForecastConfigSections.FromNode(target), conflicts);
    }

    private static Dictionary<string, JsonNode?> Leaves(JsonObject root)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        void Walk(JsonNode? node, string path)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, child) in obj)
                {
                    Walk(child, path.Length == 0 ? key : $"{path}.{key}");
                }
            }
            else
            {
                result[path] = node;
            }
        }

        Walk(root, string.Empty);
        return result;
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var node = root;
        foreach (var part in parts[..^1])
        {
            node = node[part]!.AsObject();
        }

        node[parts[^1]] = value;
    }
}
