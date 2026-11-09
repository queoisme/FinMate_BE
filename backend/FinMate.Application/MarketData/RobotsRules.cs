namespace FinMate.Application.MarketData;

/// <summary>
/// Đọc <c>robots.txt</c>. Dùng để tôn trọng trang web, không phải cơ chế bảo mật. Một trang chặn bot AI theo tên hoặc khai báo
/// <c>ai-input=no</c> vẫn có thể đọc được bảng giá, nhưng nội dung của trang đó không được gửi cho AI.
/// </summary>
public sealed class RobotsRules
{
    public const string UserAgent = "FinMateBot";

    private static readonly string[] AiAgents = ["GPTBot", "ChatGPT-User", "OAI-SearchBot", "ClaudeBot", "Claude-User", "Claude-SearchBot", "anthropic-ai", "Google-Extended", "CCBot", "PerplexityBot", "Bytespider"];

    private readonly List<Group> _groups;
    private readonly bool _aiInputForbidden;

    private sealed record Rule(bool Allow, string Path);

    private sealed class Group
    {
        public List<string> Agents { get; } = [];
        public List<Rule> Rules { get; } = [];
    }

    private RobotsRules(List<Group> groups, bool aiInputForbidden)
    {
        _groups = groups;
        _aiInputForbidden = aiInputForbidden;
    }

    /// <summary>Không có <c>robots.txt</c> thì được phép.</summary>
    public static RobotsRules AllowAll { get; } = new([], false);

    /// <summary>Trang chặn hẳn việc đọc <c>robots.txt</c>: không rõ họ cho phép gì nên coi như cấm.</summary>
    public static RobotsRules DenyAll { get; } = new([new Group { Agents = { "*" }, Rules = { new Rule(false, "/") } }], true);

    public static RobotsRules Parse(string text)
    {
        var groups = new List<Group>();
        Group? current = null;
        var collectingAgents = false;
        var aiInputForbidden = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var hash = rawLine.IndexOf('#');
            var line = (hash >= 0 ? rawLine[..hash] : rawLine).Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (field)
            {
                case "user-agent":
                    if (current is null || !collectingAgents)
                    {
                        current = new Group();
                        groups.Add(current);
                    }

                    current.Agents.Add(value);
                    collectingAgents = true;
                    break;
                case "allow" or "disallow":
                    collectingAgents = false;
                    if (current is not null && value.Length > 0)
                    {
                        current.Rules.Add(new Rule(field == "allow", value));
                    }

                    break;
                case "content-signal":
                    aiInputForbidden |= value.Replace(" ", string.Empty).Contains("ai-input=no", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return new RobotsRules(groups, aiInputForbidden);
    }

    public bool IsAllowed(string pathAndQuery) => IsAllowed(UserAgent, pathAndQuery);

    /// <summary>False khi trang khai báo không cho AI dùng nội dung, hoặc chặn toàn trang với bot AI phổ biến.</summary>
    public bool AiAllowed => !_aiInputForbidden && AiAgents.All(agent => IsAllowed(agent, "/"));

    private bool IsAllowed(string agent, string path)
    {
        var groups = _groups.Where(g => g.Agents.Any(a => a.Equals(agent, StringComparison.OrdinalIgnoreCase))).ToList();
        if (groups.Count == 0)
        {
            groups = _groups.Where(g => g.Agents.Contains("*")).ToList();
        }

        // Quy tắc khớp dài nhất thắng; bằng nhau thì Allow thắng (RFC 9309).
        Rule? best = null;
        foreach (var rule in groups.SelectMany(g => g.Rules))
        {
            if (Matches(rule.Path, path)
                && (best is null || rule.Path.Length > best.Path.Length || (rule.Path.Length == best.Path.Length && rule.Allow)))
            {
                best = rule;
            }
        }

        return best is null || best.Allow;
    }

    private static bool Matches(string pattern, string path)
    {
        var anchored = pattern.EndsWith('$');
        var parts = (anchored ? pattern[..^1] : pattern).Split('*');
        if (!path.StartsWith(parts[0], StringComparison.Ordinal))
        {
            return false;
        }

        var position = parts[0].Length;
        foreach (var part in parts.Skip(1))
        {
            var found = path.IndexOf(part, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            position = found + part.Length;
        }

        return !anchored || position == path.Length || (parts[^1].Length == 0);
    }
}
