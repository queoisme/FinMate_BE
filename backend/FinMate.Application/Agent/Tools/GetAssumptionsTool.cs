using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>Giả định đã duyệt: bằng chứng để trích dẫn, cộng tỉ lệ nguyên (điểm cơ bản) để bộ tính dùng — LLM không bao giờ tự đọc số này từ chữ.</summary>
public sealed record ApprovedAssumption(Evidence Evidence, int RateBps);

/// <summary>Công cụ tra giả định kinh tế. Không có bản đã duyệt thì trả null, không bao giờ trả một con số mặc định.</summary>
public sealed class GetAssumptionsTool(IEconomicAssumptionRepository repository)
{
    public async Task<ApprovedAssumption?> GetApprovedAsync(string key, DateTimeOffset now, CancellationToken ct = default)
    {
        var assumption = await repository.GetApprovedAsync(key, ct);
        if (assumption is null)
        {
            return null;
        }

        var evidence = new Evidence(
            Id: $"assumption:{assumption.Key}:v{assumption.Version}",
            Key: assumption.Key,
            ValueVnd: null,
            Text: $"{assumption.Key}: {Percent(assumption.RateBps)}/năm",
            Unit: assumption.Unit,
            Source: EvidenceSource.Document,
            AsOf: assumption.AsOf,
            Confidence: ConfidenceCalculator.Compute(EvidenceSource.Document, assumption.AsOf, now, 1.0),
            SourceName: assumption.SourceName,
            SourceUrl: assumption.SourceUrl);

        return new ApprovedAssumption(evidence, assumption.RateBps);
    }

    private static string Percent(int bps)
    {
        var abs = Math.Abs(bps);
        return $"{(bps < 0 ? "-" : string.Empty)}{abs / 100},{abs % 100:00}%";
    }
}
