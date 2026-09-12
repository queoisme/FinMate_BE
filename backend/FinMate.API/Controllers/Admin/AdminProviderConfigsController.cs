using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Admin;

[Route("api/v1/admin/provider-configs")]
public class AdminProviderConfigsController : AdminControllerBase
{
    private readonly IGetProviderConfigListQueryHandler _listHandler;
    private readonly ICreateProviderConfigCommandHandler _createHandler;
    private readonly IUpdateProviderConfigCommandHandler _updateHandler;
    private readonly ISetProviderConfigActivationCommandHandler _activationHandler;

    public AdminProviderConfigsController(
        IGetProviderConfigListQueryHandler listHandler,
        ICreateProviderConfigCommandHandler createHandler,
        IUpdateProviderConfigCommandHandler updateHandler,
        ISetProviderConfigActivationCommandHandler activationHandler)
    {
        _listHandler = listHandler;
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _activationHandler = activationHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var configs = await _listHandler.HandleAsync(new GetProviderConfigListQuery(includeInactive), ct);
        return Ok(ApiResponse<IReadOnlyList<ProviderConfigDto>>.Ok(configs));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProviderConfigRequest request, CancellationToken ct)
    {
        var config = await _createHandler.HandleAsync(
            new CreateProviderConfigCommand(
                CurrentAdminId, request.ProviderKey, request.DisplayName,
                request.PackageName, request.AccountType, CurrentIpAddress),
            ct);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<ProviderConfigDto>.Ok(config));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateProviderConfigRequest request, CancellationToken ct)
    {
        var config = await _updateHandler.HandleAsync(
            new UpdateProviderConfigCommand(
                CurrentAdminId, id, request.ProviderKey, request.DisplayName,
                request.PackageName, request.AccountType, CurrentIpAddress),
            ct);

        return Ok(ApiResponse<ProviderConfigDto>.Ok(config));
    }

    /// <summary>Tắt/bật. Không có DELETE: ví người dùng đã tạo vẫn trỏ tới cấu hình này.</summary>
    [HttpPatch("{id:guid}/activation")]
    public async Task<IActionResult> SetActivation(
        Guid id, [FromBody] SetActivationRequest request, CancellationToken ct)
    {
        var config = await _activationHandler.HandleAsync(
            new SetProviderConfigActivationCommand(CurrentAdminId, id, request.IsActive, CurrentIpAddress), ct);

        return Ok(ApiResponse<ProviderConfigDto>.Ok(config));
    }
}

public record CreateProviderConfigRequest(
    string ProviderKey, string DisplayName, string PackageName, AccountType AccountType);

public record UpdateProviderConfigRequest(
    string? ProviderKey, string? DisplayName, string? PackageName, AccountType? AccountType);

public record SetActivationRequest(bool IsActive);
