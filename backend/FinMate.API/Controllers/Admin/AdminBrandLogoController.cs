using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

/// <summary>
/// Tải logo ứng dụng lên Firebase Storage và trả link. Link chưa áp dụng cho tới khi admin lưu cấu hình hệ thống (theme.logoUrl), nên có đủ lý do và lịch sử phiên bản như mọi thay đổi khác.
/// Chỉ nhận PNG, JPEG, WebP (nhận dạng theo nội dung, không tin đuôi file hay Content-Type); SVG bị từ chối vì có thể chứa script.
/// </summary>
[Route("api/v1/admin/brand-logo")]
public class AdminBrandLogoController : AdminControllerBase
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private readonly IBrandLogoStorage _storage;

    public AdminBrandLogoController(IBrandLogoStorage storage) => _storage = storage;

    [HttpPost]
    [RequestSizeLimit(MaxBytes + 4096)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxBytes)
        {
            throw new BusinessRuleException("logo_invalid", "Logo phải nhỏ hơn 2 MB.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();
        var (contentType, extension) = Sniff(data) ?? throw new BusinessRuleException("logo_invalid", "Chỉ nhận ảnh PNG, JPEG hoặc WebP.");
        return Ok(ApiResponse<object>.Ok(new { url = await _storage.UploadAsync(data, contentType, extension, ct) }));
    }

    public static (string ContentType, string Extension)? Sniff(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return ("image/png", "png");
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return ("image/jpeg", "jpg");
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P') return ("image/webp", "webp");
        return null;
    }
}
