using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface IUpdateSystemCategoryCommandHandler
{
    Task<AdminCategoryDto> HandleAsync(UpdateSystemCategoryCommand command, CancellationToken ct = default);
}
