using System.Text.Json;
using System.Text.Json.Nodes;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

/// <summary>
/// "Token và chi phí AI" cho trang admin: token của trợ lý tài chính trong N ngày gần nhất (tổng, theo bước, theo ngày) do AI Service tổng hợp từ <c>agent_runs</c>, kèm chi phí ước tính.
/// Chỉ đọc. Giá KHÔNG nằm trong code: lấy từ cấu hình hệ thống <c>agent.llmInputUsdPerMillion / llmOutputUsdPerMillion</c> theo tên mô hình AI Service đang dùng.
/// Thiếu giá của mô hình đó thì <c>cost.priced = false</c> và mọi <c>cost_usd</c> là null: trang hiện token, không đoán tiền. Chi phí là ƯỚC TÍNH, vì <c>agent_runs</c> chưa ghi mô hình theo từng dòng
/// (giá của mô hình hiện tại áp cho cả khoảng) và chỉ tính các lượt của trợ lý, chưa tính các chỗ khác gọi LLM.
/// </summary>
public interface IAdminAgentUsageService
{
    Task<JsonElement?> SummaryAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default);
}

public class AdminAgentUsageService(IAiRulesAdminClient client, ISystemConfigProvider config) : IAdminAgentUsageService
{
    public async Task<JsonElement?> SummaryAsync(IReadOnlyDictionary<string, string?> query, CancellationToken ct = default)
    {
        var body = await client.SendAsync(new AiRulesRequest(HttpMethod.Get, "summary", query, Area: AiRulesRequest.AgentUsageArea), ct);
        if (body is not { ValueKind: JsonValueKind.Object } || JsonNode.Parse(body.Value.GetRawText()) is not JsonObject root)
        {
            return body;
        }

        var agent = (await config.GetAsync(ct)).Document.Agent;
        var model = root["model"]?.GetValue<string>() ?? string.Empty;
        var inputPrice = PriceOf(agent.LlmInputUsdPerMillion, model);
        var outputPrice = PriceOf(agent.LlmOutputUsdPerMillion, model);
        var priced = inputPrice is not null && outputPrice is not null;

        root["cost"] = new JsonObject
        {
            ["priced"] = priced,
            ["input_usd_per_million"] = inputPrice,
            ["output_usd_per_million"] = outputPrice,
        };
        AddCost(root["total"], inputPrice, outputPrice);
        foreach (var group in new[] { "by_step", "by_day" })
        {
            foreach (var row in root[group]?.AsArray() ?? [])
            {
                AddCost(row, inputPrice, outputPrice);
            }
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static double? PriceOf(IReadOnlyDictionary<string, double>? prices, string model)
    {
        foreach (var (key, value) in prices ?? new Dictionary<string, double>())
        {
            if (string.Equals(key.Trim(), model, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Thêm <c>cost_usd</c> vào một dòng có <c>input_tokens</c> và <c>output_tokens</c>; null khi chưa có đủ giá. Làm tròn 6 chữ số thập phân (một lượt hỏi chỉ tốn vài phần nghìn USD).</summary>
    private static void AddCost(JsonNode? row, double? inputPrice, double? outputPrice)
    {
        if (row is not JsonObject obj)
        {
            return;
        }

        var cost = inputPrice is null || outputPrice is null
            ? (double?)null
            : Math.Round(((obj["input_tokens"]?.GetValue<long>() ?? 0) * inputPrice.Value + (obj["output_tokens"]?.GetValue<long>() ?? 0) * outputPrice.Value) / 1_000_000d, 6);
        obj["cost_usd"] = cost;
    }
}
