using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Queries;

public class GetMascotInventoryQueryHandler : IGetMascotInventoryQueryHandler
{
    private readonly IGamificationRepository _gamificationRepository;

    public GetMascotInventoryQueryHandler(IGamificationRepository gamificationRepository)
    {
        _gamificationRepository = gamificationRepository;
    }

    public async Task<MascotInventoryDto> HandleAsync(
        GetMascotInventoryQuery query,
        CancellationToken ct = default)
    {
        var all = await _gamificationRepository.GetAllMascotItemsAsync(ct);
        var owned = await _gamificationRepository.GetOwnedMascotItemsAsync(query.UserId, ct);
        var ownedByItemId = owned.ToDictionary(o => o.MascotItemId);

        // Trả cả item chưa sở hữu kèm điều kiện mở khóa — đó chính là thứ thúc đẩy user,
        // giấu đi thì màn hình mascot chỉ còn là danh sách đồ đã có.
        var items = all
            .Where(i => !i.IsPremium)
            .Select(i => GamificationMapper.ToDto(i, ownedByItemId.GetValueOrDefault(i.Id)))
            .ToList();

        return new MascotInventoryDto(items);
    }
}
