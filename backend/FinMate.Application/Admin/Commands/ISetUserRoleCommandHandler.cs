using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ISetUserRoleCommandHandler
{
    Task<AdminUserDto> HandleAsync(SetUserRoleCommand command, CancellationToken ct = default);
}
