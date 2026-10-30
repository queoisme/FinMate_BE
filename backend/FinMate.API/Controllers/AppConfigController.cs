using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace FinMate.API.Controllers;

/// <summary>Cấu hình công khai cho app: số phiên bản, giờ máy chủ và toàn bộ cấu hình không bí mật.</summary>
public sealed record PublicAppConfig(int Version, DateTimeOffset ServerTime, SystemConfigDocument Config);

/// <summary>
/// Cấu hình mà app cần biết TRƯỚC khi đăng nhập: có đang bảo trì không (kèm lời nhắn), phiên bản tối thiểu, công tắc tính năng và giao diện. Ẩn danh CÓ CHỦ Ý, giống <c>/health</c>:
/// màn hình đăng nhập cũng phải hiện được popup bảo trì. Chỉ ĐỌC và chỉ chứa dữ liệu không bí mật (key bên thứ ba nằm ở kho mã hóa riêng, không bao giờ ở đây).
/// Có <c>ETag</c> theo số phiên bản và <c>Cache-Control</c> ngắn nên app hỏi thường xuyên cũng rẻ; giới hạn tần suất toàn cục theo IP vẫn áp dụng.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/app-config")]
public class AppConfigController : ControllerBase
{
    private const int MaxAgeSeconds = 30;

    private readonly ISystemConfigProvider _provider;

    public AppConfigController(ISystemConfigProvider provider)
    {
        _provider = provider;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var snapshot = await _provider.GetAsync(ct);
        var etag = $"W/\"{snapshot.Version}\"";

        Response.Headers[HeaderNames.ETag] = etag;
        Response.Headers[HeaderNames.CacheControl] = $"public, max-age={MaxAgeSeconds}";
        if (Request.Headers.IfNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(ApiResponse<PublicAppConfig>.Ok(new PublicAppConfig(snapshot.Version, DateTimeOffset.UtcNow, snapshot.Document)));
    }
}
