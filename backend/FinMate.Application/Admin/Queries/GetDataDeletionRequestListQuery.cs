using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Queries;

public record GetDataDeletionRequestListQuery(
    DataDeletionStatus? Status,
    string? Cursor,
    int Limit);
