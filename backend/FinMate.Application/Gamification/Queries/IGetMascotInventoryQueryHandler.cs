using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public interface IGetMascotInventoryQueryHandler
{
    Task<MascotInventoryDto> HandleAsync(GetMascotInventoryQuery query, CancellationToken ct = default);
}
