using FinMate.Application.Common.Models;
using FinMate.Application.Ops;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers.Ops;

[Route("api/v1/ops/me")]
public class OpsMeController : OpsControllerBase
{
    private readonly IOpsAccessService _access;

    public OpsMeController(IOpsAccessService access)
    {
        _access = access;
    }

    /// <summary>Vai trò và quyền hiện tại, đọc từ DB, để web dựng menu. Web không nên tự đoán từ token vì token có thể đã cũ.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await _access.RequireInternalAsync(CurrentActorId, ct);
        return Ok(ApiResponse<OpsMeDto>.Ok(new OpsMeDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role.ToString(),
            OpsPermissions.Names(await _access.CapabilitiesAsync(user.Role, ct)))));
    }
}

public record OpsMeDto(Guid Id, string Email, string DisplayName, string Role, IReadOnlyList<string> Capabilities);
