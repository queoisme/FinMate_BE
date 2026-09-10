namespace FinMate.Application.Categories.Commands;

public interface IDeleteCategoryCommandHandler
{
    Task HandleAsync(DeleteCategoryCommand command, CancellationToken ct = default);
}
