using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.MarketData;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

public record CreateMarketSourceRequest(string? Url, string? Name);

public record CreateMarketSourcesRequest(IReadOnlyList<string>? Urls);

public record UpdateMarketSourceRequest(string? Name, string? Kind, bool? IsActive);

public record ReviewMarketEventRequest(string? Note);

public record CorrectFuelPriceRequest(string? FuelKey, long Region1PriceCents, long Region2PriceCents, string? Note);

/// <summary>Website làm nguồn thông tin thị trường, bài đã đọc và giá xăng đã xác nhận. Xem <c>docs/market-data-api.md</c>.</summary>
[Route("api/v1/admin")]
public class AdminMarketDataController : AdminControllerBase
{
    private readonly IMarketDataService _service;
    private readonly IMarketEventService _events;

    public AdminMarketDataController(IMarketDataService service, IMarketEventService events)
    {
        _service = service;
        _events = events;
    }

    [HttpGet("market-events")]
    public async Task<IActionResult> ListEvents([FromQuery] string? status, [FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var parsed = MarketEventText.ParseStatus(status);
        if (!string.IsNullOrEmpty(status) && parsed is null)
        {
            throw new BusinessRuleException("MARKET_EVENT_STATUS_INVALID", "Trạng thái sự kiện không hợp lệ.");
        }

        return Ok(ApiResponse<MarketEventPageDto>.Ok(await _events.ListAsync(parsed, offset, limit, ct)));
    }

    [HttpPost("market-events/{id:guid}/approve")]
    public async Task<IActionResult> ApproveEvent(Guid id, [FromBody] ReviewMarketEventRequest? request, CancellationToken ct)
        => Ok(ApiResponse<MarketEventDto>.Ok(await _events.ApproveAsync(CurrentAdminId, CurrentIpAddress, id, request?.Note, ct)));

    [HttpPost("market-events/{id:guid}/reject")]
    public async Task<IActionResult> RejectEvent(Guid id, [FromBody] ReviewMarketEventRequest? request, CancellationToken ct)
        => Ok(ApiResponse<MarketEventDto>.Ok(await _events.RejectAsync(CurrentAdminId, CurrentIpAddress, id, request?.Note, ct)));

    [HttpGet("market-sources")]
    public async Task<IActionResult> ListSources(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<MarketSourceDto>>.Ok(await _service.ListSourcesAsync(ct)));

    [HttpPost("market-sources")]
    public async Task<IActionResult> CreateSource([FromBody] CreateMarketSourceRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<CreatedMarketSourceDto>.Ok(await _service.CreateSourceAsync(CurrentAdminId, CurrentIpAddress, request.Url, request.Name, ct)));

    [HttpPost("market-sources/bulk")]
    public async Task<IActionResult> CreateSources([FromBody] CreateMarketSourcesRequest request, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<BulkSourceResult>>.Ok(await _service.CreateSourcesAsync(CurrentAdminId, CurrentIpAddress, request.Urls ?? [], ct)));

    [HttpPatch("market-sources/{id:guid}")]
    public async Task<IActionResult> UpdateSource(Guid id, [FromBody] UpdateMarketSourceRequest request, CancellationToken ct)
    {
        var kind = MarketText.ParseKind(request.Kind);
        if (request.Kind is not null && kind is null)
        {
            throw new BusinessRuleException("MARKET_SOURCE_KIND_INVALID", "Loại nguồn phải là feed, price_table hoặc page.");
        }

        return Ok(ApiResponse<MarketSourceDto>.Ok(await _service.UpdateSourceAsync(CurrentAdminId, CurrentIpAddress, id, new UpdateMarketSourceInput(request.Name, kind, request.IsActive), ct)));
    }

    [HttpPost("market-sources/{id:guid}/test")]
    public async Task<IActionResult> TestSource(Guid id, CancellationToken ct)
        => Ok(ApiResponse<MarketSourceTestDto>.Ok(await _service.TestSourceAsync(id, ct)));

    [HttpGet("market-documents")]
    public async Task<IActionResult> ListDocuments(
        [FromQuery] Guid? sourceId,
        [FromQuery] string? status,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var parsed = MarketText.ParseStatus(status);
        if (!string.IsNullOrEmpty(status) && parsed is null)
        {
            throw new BusinessRuleException("MARKET_DOCUMENT_STATUS_INVALID", "Trạng thái bài không hợp lệ.");
        }

        return Ok(ApiResponse<MarketDocumentPageDto>.Ok(await _service.ListDocumentsAsync(sourceId, parsed, offset, limit, ct)));
    }

    [HttpGet("fuel-prices")]
    public async Task<IActionResult> ListFuelPrices([FromQuery] string? fuelKey, [FromQuery] int offset = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(ApiResponse<FuelPricePageDto>.Ok(await _service.ListFuelPricesAsync(fuelKey, offset, limit, ct)));

    /// <summary>Đường ngoại lệ khi nguồn hỏng hoặc sai. Bắt buộc có ghi chú, được ghi vào nhật ký admin.</summary>
    [HttpPost("fuel-prices")]
    public async Task<IActionResult> CorrectFuelPrice([FromBody] CorrectFuelPriceRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<FuelPriceDto>.Ok(await _service.CorrectFuelPriceAsync(
            CurrentAdminId, CurrentIpAddress, new CorrectFuelPriceInput(request.FuelKey ?? string.Empty, request.Region1PriceCents, request.Region2PriceCents, request.Note), ct)));
}
