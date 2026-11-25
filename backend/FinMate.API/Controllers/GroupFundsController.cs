using System.Security.Claims;
using FinMate.API.Authorization;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Groups.Funds;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Quỹ chung của nhóm (Phase 85): sổ quỹ ảo thuộc về nhóm. Cần cả cờ <c>features.groups</c> và <c>features.groupFunds</c>.</summary>
[ApiController]
[Route("api/v1/groups/{groupId:guid}/funds")]
[Authorize]
[RequireGroupsEnabled]
[RequireGroupFundsEnabled]
public class GroupFundsController : ControllerBase
{
    private readonly IGroupFundService _funds;
    private readonly IGroupFundEntryService _entries;
    private readonly IGroupFundSummaryService _summaries;
    private readonly Application.Groups.Approvals.IGroupApprovalService _approvals;

    public GroupFundsController(IGroupFundService funds, IGroupFundEntryService entries, IGroupFundSummaryService summaries, Application.Groups.Approvals.IGroupApprovalService approvals)
    {
        _approvals = approvals;
        _funds = funds;
        _entries = entries;
        _summaries = summaries;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GroupFundDto>>.Ok(await _funds.ListAsync(groupId, CurrentUserId, ct)));

    [HttpGet("{fundId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid fundId, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.GetAsync(groupId, CurrentUserId, fundId, ct)));

    [HttpPost]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateGroupFundRequest request, CancellationToken ct)
    {
        var fund = await _funds.CreateAsync(new CreateGroupFundCommand(CurrentUserId, groupId, request.Name ?? string.Empty, request.Note), ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<GroupFundDto>.Ok(fund));
    }

    [HttpPatch("{fundId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid fundId, [FromBody] UpdateGroupFundRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.UpdateAsync(new UpdateGroupFundCommand(CurrentUserId, groupId, fundId, request.Name, request.Note), ct)));

    [HttpPost("{fundId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid groupId, Guid fundId, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _funds.ArchiveAsync(groupId, CurrentUserId, fundId, ct)));

    /// <summary>BR-FUND-12: đầu kỳ, góp theo người, chi theo danh mục, rút, điều chỉnh, cuối kỳ của một tháng (giờ VN). Mặc định tháng này.</summary>
    [HttpGet("{fundId:guid}/summary")]
    public async Task<IActionResult> Summary(Guid groupId, Guid fundId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var (currentYear, currentMonth) = FinMate.Application.Common.VietnamTime.YearMonthOf(DateTimeOffset.UtcNow);
        return Ok(ApiResponse<GroupFundSummaryDto>.Ok(await _summaries.GetAsync(groupId, CurrentUserId, fundId, year ?? currentYear, month ?? currentMonth, ct)));
    }

    /// <summary>Sổ quỹ, mới nhất trước, phân trang con trỏ. Lọc theo loại dòng hoặc theo người.</summary>
    [HttpGet("{fundId:guid}/entries")]
    public async Task<IActionResult> Entries(
        Guid groupId, Guid fundId, [FromQuery] string? cursor, [FromQuery] int limit, [FromQuery] GroupFundEntryKind? kind, [FromQuery] Guid? memberId,
        CancellationToken ct)
    {
        var result = await _entries.ListAsync(groupId, CurrentUserId, new GroupFundEntryQuery(fundId, kind, memberId, cursor, limit), ct);
        return Ok(ApiResponse<IReadOnlyList<GroupFundEntryDto>>.Ok(result.Items, new ApiMeta(result.NextCursor)));
    }

    /// <summary>Ghi một dòng, luôn đứng tên người gọi. Gửi lại cùng <c>clientRequestId</c> thì trả 200 với dòng cũ.</summary>
    [HttpPost("{fundId:guid}/entries")]
    public async Task<IActionResult> CreateEntry(Guid groupId, Guid fundId, [FromBody] CreateGroupFundEntryRequest request, CancellationToken ct)
    {
        var command = new CreateGroupFundEntryCommand(
            CurrentUserId, groupId, fundId, request.Kind, request.AmountCents ?? 0, request.TransactedAt ?? DateTimeOffset.UtcNow, request.CategoryId,
            request.GroupCategoryId, request.Note, request.MerchantName, request.FinancialAccountId, request.FromTransactionId, request.ClientRequestId);

        // Chi hoặc rút từ ngưỡng duyệt do người không phải Owner ghi: 202 và body là yêu cầu chờ duyệt, quỹ chưa đổi.
        if (await _approvals.TrySubmitFundEntryAsync(command, ct) is { } pending)
        {
            return StatusCode(StatusCodes.Status202Accepted, ApiResponse<Application.Groups.Approvals.GroupApprovalDto>.Ok(pending));
        }

        var result = await _entries.CreateAsync(command, ct);
        var body = ApiResponse<GroupFundEntryResult>.Ok(result);
        return result.AlreadyExisted ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }

    /// <summary>Trường bỏ trống là giữ nguyên. <c>removeFinancialAccount</c>: bỏ liên kết ví (giao dịch cá nhân bị xoá mềm, ví được hoàn).</summary>
    [HttpPatch("{fundId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> UpdateEntry(Guid groupId, Guid fundId, Guid entryId, [FromBody] UpdateGroupFundEntryRequest request, CancellationToken ct)
        => Ok(ApiResponse<GroupFundEntryResult>.Ok(await _entries.UpdateAsync(
            new UpdateGroupFundEntryCommand(
                CurrentUserId, groupId, fundId, entryId, request.AmountCents, request.TransactedAt, request.CategoryId, request.GroupCategoryId, request.Note,
                request.MerchantName, request.FinancialAccountId, request.RemoveFinancialAccount ?? false),
            ct)));

    /// <summary>Trả quỹ sau khi xoá.</summary>
    [HttpDelete("{fundId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> DeleteEntry(Guid groupId, Guid fundId, Guid entryId, CancellationToken ct)
        => Ok(ApiResponse<GroupFundDto>.Ok(await _entries.DeleteAsync(groupId, CurrentUserId, fundId, entryId, ct)));
}

public record CreateGroupFundRequest(string? Name, string? Note);

public record UpdateGroupFundRequest(string? Name, string? Note);

public record UpdateGroupFundEntryRequest(
    long? AmountCents,
    DateTimeOffset? TransactedAt,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? Note,
    string? MerchantName,
    Guid? FinancialAccountId,
    bool? RemoveFinancialAccount);

public record CreateGroupFundEntryRequest(
    GroupFundEntryKind Kind,
    long? AmountCents,
    DateTimeOffset? TransactedAt,
    Guid? CategoryId,
    Guid? GroupCategoryId,
    string? Note,
    string? MerchantName,
    Guid? FinancialAccountId,
    Guid? FromTransactionId,
    Guid? ClientRequestId);
