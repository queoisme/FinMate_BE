using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Gamification.Commands;

public class UpdateMascotOutfitCommandHandler : IUpdateMascotOutfitCommandHandler
{
    private readonly IGamificationRepository _gamificationRepository;

    public UpdateMascotOutfitCommandHandler(IGamificationRepository gamificationRepository)
    {
        _gamificationRepository = gamificationRepository;
    }

    public async Task<MascotInventoryDto> HandleAsync(
        UpdateMascotOutfitCommand command,
        CancellationToken ct = default)
    {
        var owned = await _gamificationRepository.GetOwnedMascotItemsAsync(command.UserId, ct);
        var ownedByItemId = owned.ToDictionary(o => o.MascotItemId);

        var selected = new List<Domain.Entities.Gamification.UserMascotItem>();

        foreach (var itemId in command.ItemIds.Distinct())
        {
            // Item không sở hữu và item không tồn tại đều trả 404 — không để lộ item nào có thật.
            if (!ownedByItemId.TryGetValue(itemId, out var item))
            {
                throw new NotFoundException("MascotItem", itemId);
            }

            selected.Add(item);
        }

        var duplicateType = selected
            .GroupBy(i => i.ItemType)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateType is not null)
        {
            throw new BusinessRuleException(
                GamificationErrorCodes.DuplicateMascotSlot,
                $"Chỉ mặc được một món thuộc loại {duplicateType.Key.ToString().ToLowerInvariant()}.");
        }

        var selectedIds = selected.Select(i => i.Id).ToHashSet();
        foreach (var item in owned)
        {
            item.IsEquipped = selectedIds.Contains(item.Id);
        }

        await _gamificationRepository.SaveChangesAsync(ct);

        var all = await _gamificationRepository.GetAllMascotItemsAsync(ct);
        var refreshed = owned.ToDictionary(o => o.MascotItemId);

        return new MascotInventoryDto(all
            .Where(i => !i.IsPremium)
            .Select(i => GamificationMapper.ToDto(i, refreshed.GetValueOrDefault(i.Id)))
            .ToList());
    }
}
