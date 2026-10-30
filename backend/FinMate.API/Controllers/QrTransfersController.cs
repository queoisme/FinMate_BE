using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.QrTransfers;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Quét QR chuyển khoản (VietQR) rồi mở app ngân hàng. Backend chỉ ghi lại "ý định chuyển tiền" để sau này khớp với thông báo ngân hàng thật; FinMate không chạm vào tiền. Mọi endpoint lọc theo người
/// dùng trong JWT. Số tài khoản người nhận nằm trong BODY chứ không trên URL (URL đi vào access log).
/// </summary>
[ApiController]
[Route("api/v1/qr")]
[Authorize]
public class QrTransfersController : ControllerBase
{
    private readonly IQrTransferService _service;

    public QrTransfersController(IQrTransferService service)
    {
        _service = service;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Danh sách ngân hàng quét/mở app được, kèm mã app và việc FinMate có đọc được thông báo của nó không. App dựng liên kết mở app ngân hàng từ đây thay vì giữ bản cứng.</summary>
    [HttpGet("banks")]
    public async Task<IActionResult> Banks(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<QrBankDto>>.Ok(await _service.ListBanksAsync(ct)));

    /// <summary>Tạo ý định chuyển tiền NGAY TRƯỚC khi mở app ngân hàng. Trả 201.</summary>
    [HttpPost("transfers")]
    public async Task<IActionResult> Create([FromBody] CreateQrTransferRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(
            new CreateQrTransferCommand(
                CurrentUserId, request.ReceiverBankBin, request.ReceiverAccountNumber, request.ReceiverName,
                request.SenderBankBin, request.AmountCents, request.Message, request.CategoryId),
            ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<QrTransferDto>.Ok(created));
    }

    [HttpGet("transfers")]
    public async Task<IActionResult> List([FromQuery] QrTransferStatus? status, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<QrTransferDto>>.Ok(await _service.ListAsync(CurrentUserId, status, limit, ct)));

    /// <summary>Hủy một lần quét còn chờ (quét nhầm, hoặc không mở được app ngân hàng). Đã khớp giao dịch thật thì 409. Gọi lại với bản đã hủy vẫn 200.</summary>
    [HttpPost("transfers/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(ApiResponse<QrTransferDto>.Ok(await _service.CancelAsync(CurrentUserId, id, ct)));

    /// <summary>Danh mục đã nhớ cho người nhận này, hoặc <c>data: null</c> nếu chưa có. POST vì số tài khoản không được đặt trên URL.</summary>
    [HttpPost("counterparties/lookup")]
    public async Task<IActionResult> Lookup([FromBody] LookupCounterpartyRequest request, CancellationToken ct)
        => Ok(ApiResponse<CounterpartyOverrideDto?>.Ok(await _service.LookupOverrideAsync(CurrentUserId, request.BankBin, request.AccountNumber, ct)!));

    [HttpGet("counterparties")]
    public async Task<IActionResult> Counterparties(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CounterpartyOverrideDto>>.Ok(await _service.ListOverridesAsync(CurrentUserId, ct)));

    [HttpPatch("counterparties/{id:guid}")]
    public async Task<IActionResult> UpdateCounterparty(Guid id, [FromBody] UpdateCounterpartyRequest request, CancellationToken ct)
        => Ok(ApiResponse<CounterpartyOverrideDto>.Ok(await _service.UpdateOverrideAsync(CurrentUserId, id, request.CategoryId, ct)));

    [HttpDelete("counterparties/{id:guid}")]
    public async Task<IActionResult> DeleteCounterparty(Guid id, CancellationToken ct)
    {
        await _service.DeleteOverrideAsync(CurrentUserId, id, ct);
        return NoContent();
    }
}

public record CreateQrTransferRequest(
    string ReceiverBankBin,
    string ReceiverAccountNumber,
    string? ReceiverName,
    string SenderBankBin,
    long AmountCents,
    string? Message,
    Guid? CategoryId);

public record LookupCounterpartyRequest(string BankBin, string AccountNumber);
public record UpdateCounterpartyRequest(Guid CategoryId);
