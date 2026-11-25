using System.Security.Claims;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FinMate.Application.Loans;
using FinMate.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>
/// Sổ vay. Endpoint có từ trước Phase 90 (<c>GET/POST /loans</c>, <c>PUT/DELETE /loans/{id}</c>, <c>POST /loans/{id}/repayments</c>) giữ đường dẫn,
/// tên trường và lỗi đầu vào 422 <c>LOAN_INVALID</c> (app đang dùng); mọi thứ mới là tuỳ chọn. Endpoint mới dùng 400 <c>VALIDATION_ERROR</c> (Q7).
/// </summary>
[ApiController]
[Route("api/v1/loans")]
[Authorize]
public class LoansController(ILoanService service) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <param name="status"><c>open</c> | <c>settled</c> | <c>written_off</c> (tuỳ chọn).</param>
    /// <param name="direction"><c>borrow</c> | <c>lend</c> (tuỳ chọn).</param>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? direction, CancellationToken ct)
    {
        var items = status is null && direction is null
            ? await service.ListAsync(CurrentUserId, ct)
            : await service.ListAsync(CurrentUserId, ParseStatus(status), ParseDirection(direction), ct);
        return Ok(ApiResponse<IReadOnlyList<LoanDto>>.Ok(items));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(ApiResponse<LoanSummaryDto>.Ok(await service.SummaryAsync(CurrentUserId, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<LoanDetailDto>.Ok(await service.GetAsync(CurrentUserId, id, ct)));

    /// <summary>Tạo khoản. Gửi lại cùng <c>clientRequestId</c> → 200 với khoản đã tạo (D19); tạo mới → 201.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LoanRequest r, CancellationToken ct)
    {
        var result = await service.CreateAsync(CurrentUserId, r.ToInput(), ct);
        return StatusCode(result.AlreadyExisted ? StatusCodes.Status200OK : StatusCodes.Status201Created, ApiResponse<LoanDto>.Ok(result.Loan));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LoanRequest r, CancellationToken ct)
        => Ok(ApiResponse<LoanDto>.Ok(await service.UpdateAsync(CurrentUserId, id, r.ToInput(), ct)));

    /// <summary>Q4: xoá mềm; giao dịch đã gắn ở lại với danh mục "Cho vay / Mượn".</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Ghi một lần trả (vay) / thu hồi (cho vay). Có <c>financialAccountId</c> thì sinh giao dịch ví tương ứng. Phase 90: <c>transactionId</c> thay cho
    /// số tiền để GẮN một giao dịch đã xác nhận có sẵn; <c>clientRequestId</c> để gửi lại an toàn. Có lãi suất thì phần vượt gốc trong ngưỡng lãi thành
    /// giao dịch lãi riêng (S1).
    /// </summary>
    [HttpPost("{id:guid}/repayments")]
    public async Task<IActionResult> Repay(Guid id, [FromBody] RepaymentRequest r, CancellationToken ct)
    {
        if (r.TransactionId is { } transactionId)
        {
            return Ok(ApiResponse<LoanDto>.Ok(await service.LinkAsync(CurrentUserId, id, transactionId, LoanEntryKind.Repayment, ct)));
        }

        var result = await service.RepayAsync(CurrentUserId, id, new LoanEntryInput(r.AmountCents, r.FinancialAccountId, r.PaidAt, r.ClientRequestId), ct);
        return Ok(ApiResponse<LoanDto>.Ok(result.Loan));
    }

    /// <summary>Phase 90: cho mượn thêm / vay thêm. Cùng dạng với <c>repayments</c>.</summary>
    [HttpPost("{id:guid}/principal")]
    public async Task<IActionResult> AddPrincipal(Guid id, [FromBody] RepaymentRequest r, CancellationToken ct)
    {
        if (r.TransactionId is { } transactionId)
        {
            return Ok(ApiResponse<LoanDto>.Ok(await service.LinkAsync(CurrentUserId, id, transactionId, LoanEntryKind.Principal, ct)));
        }

        if (r.AmountCents <= 0)
        {
            throw new ValidationException([new ValidationFailure("amountCents", "Số tiền phải lớn hơn 0.")]);
        }

        var result = await service.AddPrincipalAsync(CurrentUserId, id, new LoanEntryInput(r.AmountCents, r.FinancialAccountId, r.PaidAt, r.ClientRequestId), ct);
        return Ok(ApiResponse<LoanDto>.Ok(result.Loan));
    }

    /// <summary>Phase 90: xoá một dòng (cũng là gỡ liên kết; giao dịch ở lại). Khoản thành rỗng thì bị xoá theo và trả 204.</summary>
    [HttpDelete("{id:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> DeleteEntry(Guid id, Guid entryId, CancellationToken ct)
        => await service.DeleteEntryAsync(CurrentUserId, id, entryId, ct) is { } loan ? Ok(ApiResponse<LoanDto>.Ok(loan)) : NoContent();

    /// <summary>Phase 90: xoá nợ (đóng là <c>written_off</c>); không tạo giao dịch, không đổi số dư.</summary>
    [HttpPost("{id:guid}/write-off")]
    public async Task<IActionResult> WriteOff(Guid id, CancellationToken ct)
        => Ok(ApiResponse<LoanDto>.Ok(await service.WriteOffAsync(CurrentUserId, id, ct)));

    private static LoanStatus? ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "open" => LoanStatus.Open,
        "settled" => LoanStatus.Settled,
        "written_off" => LoanStatus.WrittenOff,
        _ => throw new ValidationException([new ValidationFailure("status", "Trạng thái phải là open, settled hoặc written_off.")]),
    };

    private static LoanDirection? ParseDirection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "borrow" => LoanDirection.Borrow,
        "lend" => LoanDirection.Lend,
        _ => throw new ValidationException([new ValidationFailure("direction", "Chiều phải là borrow hoặc lend.")]),
    };
}

/// <summary>Tạo khoản kèm giao dịch giải ngân mới từ một ví (Phase 90, Q6).</summary>
public record NewLoanTransactionRequest(Guid FinancialAccountId, DateTimeOffset? TransactedAt = null);

/// <param name="Direction"><c>borrow</c> (đi vay) hoặc <c>lend</c> (cho vay).</param>
/// <param name="CreateTransaction">Phase 90 (Q6): tạo kèm giao dịch giải ngân mới từ ví. Không gửi = nợ cũ, không đụng ví (như trước).</param>
/// <param name="TransactionId">Phase 90 (Q6): giao dịch đã xác nhận có sẵn thành lần giải ngân đầu; <c>principalCents</c> phải bằng số tiền của nó.</param>
public record LoanRequest(
    string Direction, string Title, string Counterparty, long PrincipalCents, decimal InterestRatePercentYear = 0, DateOnly? DueDate = null, DateOnly? StartDate = null,
    string? Note = null, Guid? ClientRequestId = null, NewLoanTransactionRequest? CreateTransaction = null, Guid? TransactionId = null)
{
    public LoanInput ToInput() => new(
        Enum.TryParse<LoanDirection>(Direction, true, out var d)
            ? d
            : throw new BusinessRuleException(LoanErrorCodes.Invalid, "Loại khoản vay phải là borrow hoặc lend."),
        Title ?? string.Empty, Counterparty ?? string.Empty, PrincipalCents, InterestRatePercentYear, DueDate, StartDate,
        Note, ClientRequestId, CreateTransaction is null ? null : new NewLoanTransaction(CreateTransaction.FinancialAccountId, CreateTransaction.TransactedAt), TransactionId);
}

/// <param name="TransactionId">Phase 90: gắn giao dịch có sẵn thay cho <c>amountCents</c> (số tiền lấy từ giao dịch).</param>
public record RepaymentRequest(long AmountCents = 0, Guid? FinancialAccountId = null, DateTimeOffset? PaidAt = null, Guid? ClientRequestId = null, Guid? TransactionId = null);
