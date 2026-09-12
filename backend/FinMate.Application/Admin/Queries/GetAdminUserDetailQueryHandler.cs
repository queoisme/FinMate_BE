using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Queries;

public class GetAdminUserDetailQueryHandler : IGetAdminUserDetailQueryHandler
{
    private readonly IUserRepository _userRepository;

    public GetAdminUserDetailQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<AdminUserDto> HandleAsync(
        GetAdminUserDetailQuery query, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, ct)
            ?? throw new NotFoundException("User", query.UserId);

        return AdminMapper.ToDto(user);
    }
}
