using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetAdminUserDetailQueryHandler
{
    Task<AdminUserDto> HandleAsync(GetAdminUserDetailQuery query, CancellationToken ct = default);
}
