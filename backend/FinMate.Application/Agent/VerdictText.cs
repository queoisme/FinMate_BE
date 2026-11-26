using System.Text.RegularExpressions;

namespace FinMate.Application.Agent;

/// <summary>
/// Bằng chứng "verdict" mang chuỗi máy đọc (<c>verdict: infeasible (horizon 24 tháng, nhóm thu nhập stable)</c>): <see cref="AgentOrchestrator"/> cắt tiền tố để lấy kết luận.
/// Chuỗi này không được đưa nguyên cho người dùng; mọi nơi hiển thị bằng chứng (mẫu cố định, danh sách "Vì sao?") đi qua <see cref="Display"/>.
/// </summary>
public static partial class VerdictText
{
    public const string Prefix = "verdict: ";

    public static string Display(string? text)
    {
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        var rest = text[Prefix.Length..];
        var open = rest.IndexOf(' ');
        var word = open < 0 ? rest : rest[..open];
        var detail = open < 0 ? string.Empty : rest[open..].Trim();
        var label = word switch
        {
            "feasible" => "Khả thi",
            "tight" => "Cần cân nhắc",
            "infeasible" => "Khó đạt",
            _ => word,
        };

        if (detail.Length == 0)
        {
            return $"Kết luận: {label}";
        }

        // nhóm thu nhập là nhãn nội bộ của bộ dự báo; "horizon" là tiếng Anh
        detail = SegmentRegex().Replace(detail, string.Empty).Replace("horizon ", "mốc ", StringComparison.Ordinal);
        return $"Kết luận: {label} {detail}";
    }

    [GeneratedRegex(@",\s*nhóm thu nhập \w+")]
    private static partial Regex SegmentRegex();
}
