using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Application.FinancialAccounts.Queries;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/financial-accounts")]
[Authorize]
public class FinancialAccountsController : ControllerBase
{
    private readonly ICreateFinancialAccountCommandHandler _createHandler;
    private readonly IUpdateFinancialAccountCommandHandler _updateHandler;
    private readonly IToggleAccountMonitoringCommandHandler _toggleMonitoringHandler;
    private readonly IDeleteFinancialAccountCommandHandler _deleteHandler;
    private readonly IGetAccountListQueryHandler _listHandler;
    private readonly IGetAccountBalanceQueryHandler _balanceHandler;
    private readonly IGetProviderListQueryHandler _providerListHandler;

    public FinancialAccountsController(
        ICreateFinancialAccountCommandHandler createHandler,
        IUpdateFinancialAccountCommandHandler updateHandler,
        IToggleAccountMonitoringCommandHandler toggleMonitoringHandler,
        IDeleteFinancialAccountCommandHandler deleteHandler,
        IGetAccountListQueryHandler listHandler,
        IGetAccountBalanceQueryHandler balanceHandler,
        IGetProviderListQueryHandler providerListHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _toggleMonitoringHandler = toggleMonitoringHandler;
        _deleteHandler = deleteHandler;
        _listHandler = listHandler;
        _balanceHandler = balanceHandler;
        _providerListHandler = providerListHandler;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Danh sách app tài chính để người dùng tick chọn ở onboarding (docx Bước 1.2).
    ///
    /// Đặt TRƯỚC <c>GET ""</c> và dùng đường dẫn hằng nên không đụng route <c>{id:guid}</c>.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var providers = await _providerListHandler.HandleAsync(new GetProviderListQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<ProviderOptionDto>>.Ok(providers));
    }

    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var accounts = await _listHandler.HandleAsync(new GetAccountListQuery(CurrentUserId), ct);
        return Ok(ApiResponse<IReadOnlyList<FinancialAccountDto>>.Ok(accounts));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFinancialAccountRequest request, CancellationToken ct)
    {
        var account = await _createHandler.HandleAsync(
            new CreateFinancialAccountCommand(
                CurrentUserId,
                request.AccountName,
                request.AccountType,
                request.ProviderConfigId,
                request.InitialBalanceCents),
            ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<FinancialAccountDto>.Ok(account));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFinancialAccountRequest request, CancellationToken ct)
    {
        var account = await _updateHandler.HandleAsync(
            new UpdateFinancialAccountCommand(CurrentUserId, id, request.AccountName), ct);
        return Ok(ApiResponse<FinancialAccountDto>.Ok(account));
    }

    [HttpPatch("{id:guid}/monitoring")]
    public async Task<IActionResult> ToggleMonitoring(Guid id, [FromBody] ToggleMonitoringRequest request, CancellationToken ct)
    {
        var account = await _toggleMonitoringHandler.HandleAsync(
            new ToggleAccountMonitoringCommand(CurrentUserId, id, request.IsMonitored), ct);
        return Ok(ApiResponse<FinancialAccountDto>.Ok(account));
    }

    [HttpGet("{id:guid}/balance")]
    public async Task<IActionResult> GetBalance(Guid id, CancellationToken ct)
    {
        var balance = await _balanceHandler.HandleAsync(new GetAccountBalanceQuery(CurrentUserId, id), ct);
        return Ok(ApiResponse<AccountBalanceDto>.Ok(balance));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _deleteHandler.HandleAsync(new DeleteFinancialAccountCommand(CurrentUserId, id), ct);
        return NoContent();
    }
}

public record CreateFinancialAccountRequest(
    string AccountName,
    AccountType AccountType,
    Guid? ProviderConfigId,
    long InitialBalanceCents = 0);

public record UpdateFinancialAccountRequest(string AccountName);
public record ToggleMonitoringRequest(bool IsMonitored);
