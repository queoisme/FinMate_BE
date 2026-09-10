using FinMate.Application.Common.Models;

namespace FinMate.Application.Categories.Commands;

public interface ICreateCategoryCommandHandler
{
    Task<CategoryDto> HandleAsync(CreateCategoryCommand command, CancellationToken ct = default);
}
