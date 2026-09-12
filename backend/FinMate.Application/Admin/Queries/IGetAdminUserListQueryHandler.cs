using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public interface IGetAdminUserListQueryHandler
{
    Task<AdminUserListDto> HandleAsync(GetAdminUserListQuery query, CancellationToken ct = default);
}
