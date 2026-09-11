using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Commands;

public interface IUpdateMascotOutfitCommandHandler
{
    Task<MascotInventoryDto> HandleAsync(UpdateMascotOutfitCommand command, CancellationToken ct = default);
}
