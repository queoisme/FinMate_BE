using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.MarketData;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class CreateSavingGoalCommandHandler : ICreateSavingGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IValidator<CreateSavingGoalCommand> _validator;
    private readonly IFinanceMethodRepository _financeMethods;
    private readonly IUserMethodRepository _userMethods;
    private readonly GoalMarketService? _market;

    public CreateSavingGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IValidator<CreateSavingGoalCommand> validator,
        IFinanceMethodRepository financeMethods,
        IUserMethodRepository userMethods,
        GoalMarketService? market = null)
    {
        _savingGoalRepository = savingGoalRepository;
        _validator = validator;
        _financeMethods = financeMethods;
        _userMethods = userMethods;
        _market = market;
    }

    public async Task<SavingGoalDto> HandleAsync(CreateSavingGoalCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        GoalTemplate? template = null;
        if (command.GoalTemplateCode is { } templateCode)
        {
            template = await _financeMethods.GetTemplateByCodeAsync(templateCode, ct)
                ?? throw new NotFoundException("GoalTemplate", templateCode);
            if (!template.IsActive)
            {
                throw new BusinessRuleException(FinanceErrorCodes.GoalTemplateInactive, "Mẫu mục tiêu này hiện không còn được cung cấp.");
            }
        }

        // Ghi nhận cách quản lý CHÍNH đang dùng lúc tạo để biết mục tiêu này theo goal_mode nào. Chưa dùng cách nào thì để trống.
        var primary = (await _userMethods.GetActiveAsync(command.UserId, ct)).FirstOrDefault(m => m.Role == UserMethodRole.Primary);

        var now = DateTimeOffset.UtcNow;
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            Name = command.Name,
            TargetCents = command.TargetCents,
            SavedCents = command.InitialSavedCents,
            InitialSavedCents = command.InitialSavedCents,
            Icon = string.IsNullOrWhiteSpace(command.Icon) ? null : command.Icon.Trim(),
            ContributionFrequency = command.ContributionFrequency,
            Status = command.IsDraft ? SavingGoalStatus.Draft : SavingGoalStatus.Active,
            Deadline = command.Deadline,
            GoalTemplateId = template?.Id,
            UserMethodId = primary?.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (_market is not null && !string.IsNullOrEmpty(command.MarketTopic))
        {
            await _market.ApplyAsync(goal, command.MarketTopic, targetChanged: false, now, ct);
        }

        await _savingGoalRepository.AddAsync(goal, ct);

        return SavingGoalMapper.ToDto(goal);
    }
}
