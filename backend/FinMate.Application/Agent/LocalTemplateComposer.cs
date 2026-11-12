using System.Globalization;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>
/// Câu trả lời dựng THẲNG từ bằng chứng khi không soạn được bằng LLM (ai-service sập giữa vòng, hoặc mọi mệnh đề bị loại vì mất bằng chứng). Cùng quy tắc với bản ở ai-service
/// (<c>template_composer.py</c>): mỗi mệnh đề là một bằng chứng kèm nguồn và ngày cập nhật theo giờ Việt Nam, số tiền in đúng giá trị của chính bằng chứng đó. Tồn tại riêng ở backend
/// vì lúc này không thể nhờ ai-service. Khô hơn câu LLM viết nhưng luôn đúng.
/// </summary>
public static class LocalTemplateComposer
{
    public const int MaxClaims = 12;

    public static IReadOnlyList<AgentClaim> Compose(IReadOnlyList<Evidence> evidence)
    {
        var claims = new List<AgentClaim>();
        foreach (var item in evidence)
        {
            if (claims.Count >= MaxClaims)
            {
                break;
            }

            string body;
            if (item.ValueVnd is { } value)
            {
                body = $"{item.Text ?? item.Key}: {FormatVnd(value)}";
            }
            else if (!string.IsNullOrWhiteSpace(item.Text))
            {
                body = item.Text;
            }
            else
            {
                continue;
            }

            var stamp = $"{SourceLabel(item.Source)}, cập nhật {VietnamTime.ToLocal(item.AsOf).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            claims.Add(new AgentClaim($"{body} ({stamp}).", [item.Id]));
        }

        return claims;
    }

    /// <summary>1500000 → "1.500.000 đồng" (dấu chấm ngăn nghìn); không phụ thuộc dữ liệu văn hoá của máy chủ.</summary>
    public static string FormatVnd(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") + " đồng";

    private static string SourceLabel(EvidenceSource source) => source switch
    {
        EvidenceSource.Confirmed => "đã xác nhận",
        EvidenceSource.UserEntered => "bạn nhập",
        EvidenceSource.Estimated => "ước tính",
        EvidenceSource.Document => "theo tài liệu",
        _ => "không rõ nguồn",
    };
}
