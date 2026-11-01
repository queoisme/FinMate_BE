using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Trạng thái và thử câu của RASA cho màn hình admin trên web. Rasa nằm trong mạng docker nội bộ nên trình duyệt không tới được: controller này chỉ giữ cổng (AdminOnly)
/// và chuyển tiếp tới <c>/api/v1/admin/rasa</c> của AI Service. Xem trạng thái, thử câu, liệt kê MỌI phiên bản model đã huấn luyện và chọn một phiên bản để áp dụng
/// (qua cổng đo như <c>promote</c>; ép được kèm lý do). Huấn luyện và sửa dữ liệu mẫu không có API vì đi qua <c>scripts/nlu_model.py</c>.
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

    /// <summary>
    /// MỌI phiên bản model Rasa đã huấn luyện và đăng ký: trạng thái (active/candidate/retired/rejected), ngày, số câu học, số đo (Rasa thô và sau luật, theo từng bộ test),
    /// ghi chú, và <c>gate</c> = bản này có qua cổng đo để thay bản đang chạy không (kèm lý do nếu không). Có thêm <c>running_model_file</c> (tệp Rasa đang nạp thật),
    /// <c>in_sync</c> (sổ và Rasa khớp nhau) và <c>can_activate</c> (AI Service có quyền chép model để kích hoạt).
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> Models(CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ModelsAsync(ct)));

    /// <summary>
    /// Áp dụng một phiên bản (kể cả quay về bản cũ): trả <b>202</b> ngay, Rasa nạp nóng ở nền (vài phút; chat chạy bằng luật trong lúc nạp, không ngừng dịch vụ), KHÔNG cần khởi động lại, và khởi động lại sau này vẫn đúng bản. Theo dõi ở <c>activation</c> (<c>state</c> loading/done/failed, <c>error</c>) của <c>GET models</c>. Kích hoạt lại chính bản đang chạy khi <c>in_sync</c> là false là nạp lại cho khớp (<c>resync</c>). Thân JSON (có thể <c>{}</c>):
    /// <c>expected_active</c> = bản đang chạy mà bạn nhìn thấy (khác thực tế thì 409 <c>stale_active</c>), <c>force</c> + <c>reason</c> (≥ 3 ký tự) để áp dụng bản không qua cổng đo.
    /// Lỗi: 404 không có bản / thiếu tệp, 409 đang chạy rồi / người khác vừa đổi / đang có lần nạp khác (<c>activation_in_progress</c>), 422 <c>blocked_by_gate</c> (kèm lý do trong <c>details</c>) hoặc thiếu lý do,
    /// 503 chưa cấu hình. Rasa nạp hỏng thì lỗi nằm ở <c>activation.error</c> và model cũ giữ nguyên. Mỗi lần thành công ghi một dòng nhật ký admin.
    /// </summary>
    [HttpPost("models/{version}/activate")]
    public async Task<IActionResult> ActivateModel(string version, [FromBody] JsonElement body, CancellationToken ct)
        => StatusCode(StatusCodes.Status202Accepted, ApiResponse<JsonElement?>.Ok(await _service.ActivateModelAsync(new AdminRasaModelActivation(CurrentAdminId, version, body, CurrentIpAddress), ct)));

    /// <summary>Thử một câu (tối đa 500 ký tự): ý định, 5 ý định xếp đầu và các thực thể. Là POST vì có thân, nhưng không ghi gì.</summary>
    [HttpPost("parse")]
    public async Task<IActionResult> Parse([FromBody] JsonElement body, CancellationToken ct)
        => Ok(ApiResponse<JsonElement?>.Ok(await _service.ParseAsync(body, ct)));
}
