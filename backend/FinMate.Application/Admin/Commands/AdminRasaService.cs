using System.Text.Json;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Admin.Commands;

public interface IAdminRasaService
{
    /// <summary>Trạng thái Rasa (phiên bản, model đang chạy, số lượt huấn luyện) từ AI Service. Rasa tắt thì AI Service trả <c>available: false</c> (200).</summary>
    Task<JsonElement?> StatusAsync(CancellationToken ct = default);

    /// <summary>Ý định, xếp hạng và thực thể Rasa đọc được từ một câu. Chỉ đọc: không ghi gì, không để lại nhật ký.</summary>
    Task<JsonElement?> ParseAsync(JsonElement body, CancellationToken ct = default);
}

public class AdminRasaService : IAdminRasaService
{
    private readonly IAiRulesAdminClient _client;

    public AdminRasaService(IAiRulesAdminClient client)
    {
        _client = client;
    }

    public Task<JsonElement?> StatusAsync(CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Get, "status", Area: AiRulesRequest.RasaArea), ct);

    public Task<JsonElement?> ParseAsync(JsonElement body, CancellationToken ct = default)
        => _client.SendAsync(new AiRulesRequest(HttpMethod.Post, "parse", Body: body, Area: AiRulesRequest.RasaArea), ct);
}
