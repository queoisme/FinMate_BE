using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.SavingGoals.Commands;

public class CreateSavingGoalCommandHandler : ICreateSavingGoalCommandHandler
{
    private readonly ISavingGoalRepository _savingGoalRepository;
    private readonly IValidator<CreateSavingGoalCommand> _validator;

    public CreateSavingGoalCommandHandler(
        ISavingGoalRepository savingGoalRepository,
        IValidator<CreateSavingGoalCommand> validator)
    {
        _savingGoalRepository = savingGoalRepository;
        _validator = validator;
    }

    public async Task<SavingGoalDto> HandleAsync(CreateSavingGoalCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var now = DateTimeOffset.UtcNow;
        var goal = new SavingGoal
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            Name = command.Name,
            TargetCents = command.TargetCents,
            SavedCents = 0,
            Status = SavingGoalStatus.Active,
            Deadline = command.Deadline,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _savingGoalRepository.AddAsync(goal, ct);

        return SavingGoalMapper.ToDto(goal);
    }
}
