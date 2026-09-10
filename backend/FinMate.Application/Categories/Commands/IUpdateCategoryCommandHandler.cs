using FinMate.Application.Common.Models;

namespace FinMate.Application.Categories.Commands;

public interface IUpdateCategoryCommandHandler
{
    Task<CategoryDto> HandleAsync(UpdateCategoryCommand command, CancellationToken ct = default);
}
