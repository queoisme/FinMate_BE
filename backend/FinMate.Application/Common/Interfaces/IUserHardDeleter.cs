namespace FinMate.Application.Common.Interfaces;

/// <summary>Physically removes a user row — used only by DataDeletionJob after the 30-day retention window.</summary>
public interface IUserHardDeleter
{
    Task HardDeleteAsync(Guid userId, CancellationToken ct = default);
}
