using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Agent;

/// <summary>Giả định đã duyệt: bằng chứng để trích dẫn, cộng tỉ lệ nguyên (điểm cơ bản) để bộ tính dùng — LLM không bao giờ tự đọc số này từ chữ.</summary>
public sealed record ApprovedAssumption(Evidence Evidence, int RateBps);

/// <summary>Công cụ tra giả định kinh tế. Không có bản đã duyệt thì trả null, không bao giờ trả một con số mặc định.</summary>
public sealed class GetAssumptionsTool(IEconomicAssumptionRepository repository, ISystemConfigProvider? config = null)
{
    /// <summary>Các khóa có bản đã duyệt, để mô tả công cụ cho planner.</summary>
    public Task<IReadOnlyList<string>> ApprovedKeysAsync(CancellationToken ct = default) => repository.ListApprovedKeysAsync(ct);

    public async Task<ApprovedAssumption?> GetApprovedAsync(string key, DateTimeOffset now, CancellationToken ct = default)
    {
        var assumption = await repository.GetApprovedAsync(key, ct);
        if (assumption is null)
        {
            return null;
        }

        // Câu chữ cho người đọc nằm ở mẫu (admin sửa được), không để khóa kỹ thuật như general_price_growth lọt ra màn hình; khóa chưa có mẫu riêng dùng mẫu chung.
        var texts = config is null ? null : (await config.GetAsync(ct)).Document.Agent.Texts;
        var templateKey = $"assumption_{assumption.Key}";
        var text = AgentTexts.Render(texts, AgentTexts.Catalog.ContainsKey(templateKey) ? templateKey : "assumption_generic", ("rate", Percent(assumption.RateBps)));
        var evidence = new Evidence(
            Id: $"assumption:{assumption.Key}:v{assumption.Version}",
            Key: assumption.Key,
            ValueVnd: null,
            Text: text,
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
