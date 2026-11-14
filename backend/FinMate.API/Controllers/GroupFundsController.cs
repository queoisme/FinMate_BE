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

    public GroupFundsController(IGroupFundService funds, IGroupFundEntryService entries)
    {
        _funds = funds;
        _entries = entries;
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
        var result = await _entries.CreateAsync(
            new CreateGroupFundEntryCommand(
                CurrentUserId, groupId, fundId, request.Kind, request.AmountCents ?? 0, request.TransactedAt ?? DateTimeOffset.UtcNow, request.CategoryId,
                request.GroupCategoryId, request.Note, request.MerchantName, request.FinancialAccountId, request.FromTransactionId, request.ClientRequestId),
            ct);
        var body = ApiResponse<GroupFundEntryResult>.Ok(result);
        return result.AlreadyExisted ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    }
}

public record CreateGroupFundRequest(string? Name, string? Note);

public record UpdateGroupFundRequest(string? Name, string? Note);

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
