namespace FinMate.Application.Agent;

/// <summary>Danh sách công cụ cho phép. Công cụ không có ở đây thì agent không gọi được, bất kể LLM đề xuất gì.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools) => _tools = tools.ToDictionary(t => t.Spec.Name);

    public IReadOnlyCollection<string> Allowlist => _tools.Keys;

    public IReadOnlyList<ToolSpec> Specs => _tools.Values.Select(t => t.Spec).ToArray();

    /// <summary>Mô tả gửi cho planner: công cụ có mô tả theo dữ liệu hiện có thì lấy bản đó; lỗi lúc lấy (DB...) thì quay về mô tả tĩnh, không làm hỏng việc lập kế hoạch.</summary>
    public async Task<IReadOnlyList<ToolSpec>> SpecsAsync(CancellationToken ct = default)
    {
        var specs = new List<ToolSpec>(_tools.Count);
        foreach (var tool in _tools.Values)
        {
            if (tool is IRuntimeDescribedTool described)
            {
                try
                {
                    specs.Add(await described.GetSpecAsync(ct));
                    continue;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // rơi xuống mô tả tĩnh bên dưới
                }
            }

            specs.Add(tool.Spec);
        }

        return specs;
    }

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);
}
