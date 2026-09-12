using FinMate.Application.Common.Models;

namespace FinMate.Application.Admin.Commands;

public interface ICreateSystemCategoryCommandHandler
{
    Task<AdminCategoryDto> HandleAsync(CreateSystemCategoryCommand command, CancellationToken ct = default);
}
