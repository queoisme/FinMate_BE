using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Trạng thái và thử câu của RASA cho màn hình admin trên web. Rasa nằm trong mạng docker nội bộ nên trình duyệt không tới được: controller này chỉ giữ cổng (AdminOnly)
/// và chuyển tiếp tới <c>/api/v1/admin/rasa</c> của AI Service. CHỈ ĐỌC. Huấn luyện và sửa dữ liệu mẫu không có API vì đi qua <c>scripts/nlu_model.py</c> với cổng đo trước khi thay model.
/// </summary>
[Route("api/v1/admin/rasa")]
public class AdminRasaController : AdminControllerBase
{
    private readonly IAdminRasaService _service;

    public AdminRasaController(IAdminRasaService service)
    {
        _service = service;
    }

    /// <summary>Phiên bản Rasa, model đang chạy và số lượt huấn luyện đang chạy. <c>available: false</c> khi Rasa tắt hoặc chưa cấu hình (<c>configured</c>).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.StatusAsync(ct)));

    /// <summary>Thử một câu (tối đa 500 ký tự): ý định, 5 ý định xếp đầu và các thực thể. Là POST vì có thân, nhưng không ghi gì.</summary>
    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ParseAsync(body, ct)));
}
