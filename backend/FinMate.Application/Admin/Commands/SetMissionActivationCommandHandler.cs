using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public class SetMissionActivationCommandHandler : ISetMissionActivationCommandHandler
{
    private readonly IMissionRepository _missionRepository;
    private readonly IAuditLogService _auditLogService;

    public SetMissionActivationCommandHandler(
        IMissionRepository missionRepository,
        IAuditLogService auditLogService)
    {
        _missionRepository = missionRepository;
        _auditLogService = auditLogService;
    }

    public async Task<AdminMissionDto> HandleAsync(
        SetMissionActivationCommand command, CancellationToken ct = default)
    {
        var mission = await _missionRepository.GetMissionByIdAsync(command.MissionId, ct)
            ?? throw new NotFoundException("Mission", command.MissionId);

        if (mission.IsActive == command.IsActive)
        {
            return AdminMapper.ToDto(mission);
        }

        mission.IsActive = command.IsActive;
        mission.UpdatedAt = DateTimeOffset.UtcNow;
        await _missionRepository.UpdateMissionAsync(mission, ct);

        // Cache user:{id}:active_missions theo TỪNG người dùng nên không invalidate hàng loạt
        // được — key không liệt kê ra được. Nhiệm vụ vừa tắt còn hiện ở client tới hết TTL 30
        // phút (CacheKeys.ActiveMissionsTtl); TTL chính là biên trên của độ trễ này.
        await _auditLogService.LogAsync(
            AuditEvents.AdminMissionActivationChanged,
            command.AdminId,
            command.IpAddress,
            new { mission.Id, mission.Code, mission.IsActive },
            ct);

        return AdminMapper.ToDto(mission);
    }
}
