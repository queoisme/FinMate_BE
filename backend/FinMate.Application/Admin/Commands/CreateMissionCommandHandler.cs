using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities.Gamification;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class CreateMissionCommandHandler : ICreateMissionCommandHandler
{
    private readonly IMissionRepository _missionRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<CreateMissionCommand> _validator;

    public CreateMissionCommandHandler(
        IMissionRepository missionRepository,
        IAuditLogService auditLogService,
        IValidator<CreateMissionCommand> validator)
    {
        _missionRepository = missionRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminMissionDto> HandleAsync(
        CreateMissionCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var code = command.Code.Trim().ToLowerInvariant();
        if (await _missionRepository.GetByCodeAsync(code, ct) is not null)
        {
            throw new ConflictException(
                AdminErrorCodes.MissionCodeDuplicate,
                $"Đã có nhiệm vụ với mã '{code}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var mission = new Mission
        {
            Code = code,
            Title = command.Title.Trim(),
            Description = command.Description.Trim(),
            PeriodType = command.PeriodType,
            ConditionType = command.ConditionType,
            ConditionTarget = command.ConditionTarget,
            ExpReward = command.ExpReward,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _missionRepository.AddMissionAsync(mission, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminMissionCreated,
            command.AdminId,
            command.IpAddress,
            new { mission.Id, mission.Code, mission.PeriodType, mission.ConditionType },
            ct);

        return AdminMapper.ToDto(mission);
    }
}
