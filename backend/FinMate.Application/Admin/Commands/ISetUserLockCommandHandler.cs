using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ISetUserLockCommandHandler
{
    Task<AdminUserDto> HandleAsync(SetUserLockCommand command, CancellationToken ct = default);
}
