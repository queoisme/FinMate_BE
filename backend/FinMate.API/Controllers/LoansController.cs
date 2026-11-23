using System.Security.Claims;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.Loans;
using FinMate.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

[ApiController]
[Route("api/v1/loans")]
[Authorize]
public class LoansController(ILoanService service) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<LoanDto>>.Ok(await service.ListAsync(CurrentUserId, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LoanRequest r, CancellationToken ct)
    {
        var result = await service.CreateAsync(CurrentUserId, r.ToInput(), ct);
        return StatusCode(result.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created, ApiResponse<LoanDto>.Ok(result.Loan));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LoanRequest r, CancellationToken ct)
        => Ok(ApiResponse<LoanDto>.Ok(await service.UpdateAsync(CurrentUserId, id, r.ToInput(), ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>Ghi một lần trả (vay) / thu hồi (cho vay). Có <c>financialAccountId</c> thì sinh giao dịch ví tương ứng.</summary>
    [HttpPost("{id:guid}/repayments")]
    public async Task<IActionResult> Repay(Guid id, [FromBody] RepaymentRequest r, CancellationToken ct)
        => Ok(ApiResponse<LoanDto>.Ok((await service.RepayAsync(CurrentUserId, id, new LoanEntryInput(r.AmountCents, r.FinancialAccountId, r.PaidAt), ct)).Loan));
}

/// <param name="Direction"><c>borrow</c> (đi vay) hoặc <c>lend</c> (cho vay).</param>
public record LoanRequest(string Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear = 0, DateOnly? DueDate = null, DateOnly? StartDate = null)
{
    public LoanInput ToInput() => new(
        Enum.TryParse<LoanDirection>(Direction, true, out var d)
            ? d
            : throw new BusinessRuleException(LoanErrorCodes.Invalid, "Loại khoản vay phải là borrow hoặc lend."),
        Title ?? string.Empty, Counterparty ?? string.Empty, PrincipalCents, InterestRatePercentYear, DueDate, StartDate);
}

public record RepaymentRequest(long AmountCents, Guid? FinancialAccountId = null, DateTimeOffset? PaidAt = null);
