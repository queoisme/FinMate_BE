namespace FinMate.Application.Common.Models;

public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? IconName,
    bool IsSystem,
    DateTimeOffset CreatedAt);
