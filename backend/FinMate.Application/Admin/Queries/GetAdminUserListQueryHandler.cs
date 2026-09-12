using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetAdminUserListQueryHandler : IGetAdminUserListQueryHandler
{
    private readonly IUserRepository _userRepository;

    public GetAdminUserListQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<AdminUserListDto> HandleAsync(
        GetAdminUserListQuery query, CancellationToken ct = default)
    {
        var result = await _userRepository.GetPageAsync(
            new UserListFilter(
                query.Search,
                query.Role,
                query.IsLocked,
                query.IncludeDeleted,
                query.Cursor,
                AdminPaging.Clamp(query.Limit)),
            ct);

        return new AdminUserListDto(
            result.Items.Select(AdminMapper.ToDto).ToList(),
            result.NextCursor);
    }
}
