using FinMate.Application.Common.Models;
using FinMate.Application.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Điều khoản sử dụng và Chính sách quyền riêng tư. Công khai: màn đăng ký phải cho đọc trước khi có tài khoản.
/// Chỉ trả bản đã xuất bản; nháp của Manager không bao giờ lộ ra đây.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/legal")]
public class LegalController : ControllerBase
{
    private readonly ILegalDocumentService _legal;

    public LegalController(ILegalDocumentService legal)
    {
        _legal = legal;
    }

    [HttpGet("terms")]
    public async Task<IActionResult> Terms(CancellationToken ct)
        => Ok(ApiResponse<LegalDocumentDto>.Ok(await _legal.GetPublishedAsync(LegalDocumentKind.Terms, ct)));

    [HttpGet("privacy")]
    public async Task<IActionResult> Privacy(CancellationToken ct)
        => Ok(ApiResponse<LegalDocumentDto>.Ok(await _legal.GetPublishedAsync(LegalDocumentKind.Privacy, ct)));
}
