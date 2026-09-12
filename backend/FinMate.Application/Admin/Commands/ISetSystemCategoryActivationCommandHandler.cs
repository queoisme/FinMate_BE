using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ISetSystemCategoryActivationCommandHandler
{
    Task<AdminCategoryDto> HandleAsync(
        SetSystemCategoryActivationCommand command, CancellationToken ct = default);
}
