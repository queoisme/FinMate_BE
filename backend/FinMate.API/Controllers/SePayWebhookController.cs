using System.Text.Json.Serialization;
using FinMate.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FinMate.API.Controllers;

/// <summary>
/// IPN của SePay. Ẩn danh vì SePay không có JWT, thay vào đó kiểm <c>X-Secret-Key</c>. IPN chỉ là tín hiệu: quyền lợi được cấp sau khi backend tự hỏi lại SePay.
/// Trả dạng SePay yêu cầu, không dùng <c>ApiResponse</c>. Không ghi log body, vì nó có số tiền và thông tin thẻ.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/webhooks/sepay")]
public class SePayWebhookController : ControllerBase
{
    private const string KeyHeader = "X-Secret-Key";
    private const string NotificationVoid = "TRANSACTION_VOID";
    private const string NotificationPaid = "ORDER_PAID";
    private const int MaxBodyBytes = 64 * 1024;

    private readonly ISubscriptionSettlementService _settlement;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(ISubscriptionSettlementService settlement, ILogger<SePayWebhookController> logger)
    {
        _settlement = settlement;
        _logger = logger;
    }

    [HttpPost("ipn")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Ipn([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SePayIpnRequest? request, CancellationToken ct)
    {
        if (!await _settlement.IsValidIpnKeyAsync(Request.Headers[KeyHeader].ToString(), ct))
        {
            return Unauthorized();
        }

        var invoice = request?.Order?.InvoiceNumber;
        if (string.IsNullOrWhiteSpace(invoice))
        {
            _logger.LogWarning("IPN SePay không có mã đơn");
        }
        else if (request?.NotificationType == NotificationPaid)
        {
            await _settlement.SettleOrderAsync(invoice, ct);
        }
        else if (request?.NotificationType == NotificationVoid)
        {
            await _settlement.VoidOrderAsync(invoice, request.Transaction?.Id, ct);
        }

        return Ok(new { success = true });
    }
}

public record SePayIpnRequest(
    [property: JsonPropertyName("notification_type")] string? NotificationType,
    [property: JsonPropertyName("order")] SePayIpnOrder? Order,
    [property: JsonPropertyName("transaction")] SePayIpnTransaction? Transaction = null);

public record SePayIpnOrder([property: JsonPropertyName("order_invoice_number")] string? InvoiceNumber);

public record SePayIpnTransaction([property: JsonPropertyName("id")] string? Id);
