using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.Application.Admin.Commands;

public class UpdateMissionCommandHandler : IUpdateMissionCommandHandler
{
    private readonly IMissionRepository _missionRepository;
    private readonly IAuditLogService _auditLogService;
    private readonly IValidator<UpdateMissionCommand> _validator;

    public UpdateMissionCommandHandler(
        IMissionRepository missionRepository,
        IAuditLogService auditLogService,
        IValidator<UpdateMissionCommand> validator)
    {
        _missionRepository = missionRepository;
        _auditLogService = auditLogService;
        _validator = validator;
    }

    public async Task<AdminMissionDto> HandleAsync(
        UpdateMissionCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var mission = await _missionRepository.GetMissionByIdAsync(command.MissionId, ct)
            ?? throw new NotFoundException("Mission", command.MissionId);

        if (command.Code is not null
            && !string.Equals(command.Code.Trim(), mission.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField,
                "Không đổi được mã nhiệm vụ: MissionSeeder và GamificationService tra theo nó.");
        }

        // period_type và condition_type bất biến: user_missions của chu kỳ ĐANG CHẠY đã tích
        // tiến độ theo điều kiện cũ. Đổi giữa chừng làm tiến độ đã có trở thành vô nghĩa —
        // hoặc người dùng mất công đã bỏ ra, hoặc họ hoàn thành ngay mà chưa làm gì.
        if (command.PeriodType is { } periodType && periodType != mission.PeriodType)
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField,
                "Không đổi được chu kỳ nhiệm vụ khi nó đã tồn tại — tạo nhiệm vụ mới và tắt cái cũ.");
        }

        if (command.ConditionType is { } conditionType && conditionType != mission.ConditionType)
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField,
                "Không đổi được loại điều kiện khi nhiệm vụ đã tồn tại — tiến độ đang dở của "
                + "người dùng được tính theo điều kiện cũ. Tạo nhiệm vụ mới và tắt cái cũ.");
        }

        if (command.Title is not null)
        {
            mission.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            mission.Description = command.Description.Trim();
        }

        // Target và thưởng đổi được: chúng chỉ là con số so sánh, có hiệu lực từ lần
        // MissionResetJob kế tiếp mà không làm hỏng tiến độ đã tích.
        if (command.ConditionTarget is { } target)
        {
            mission.ConditionTarget = target;
        }

        if (command.ExpReward is { } reward)
        {
            mission.ExpReward = reward;
        }

        mission.UpdatedAt = DateTimeOffset.UtcNow;
        await _missionRepository.UpdateMissionAsync(mission, ct);

        await _auditLogService.LogAsync(
            AuditEvents.AdminMissionUpdated,
            command.AdminId,
            command.IpAddress,
            new { mission.Id, mission.Code, mission.ConditionTarget, mission.ExpReward },
            ct);

        return AdminMapper.ToDto(mission);
    }
}
