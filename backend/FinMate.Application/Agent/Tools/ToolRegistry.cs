namespace FinMate.Application.Agent;

/// <summary>Danh sách công cụ cho phép. Công cụ không có ở đây thì agent không gọi được, bất kể LLM đề xuất gì.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools) => _tools = tools.ToDictionary(t => t.Spec.Name);

    public IReadOnlyCollection<string> Allowlist => _tools.Keys;

    public IReadOnlyList<ToolSpec> Specs => _tools.Values.Select(t => t.Spec).ToArray();

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);
}
