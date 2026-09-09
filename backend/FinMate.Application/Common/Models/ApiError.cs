namespace FinMate.Application.Common.Models;

public record ApiError(string Code, string Message, IReadOnlyList<ApiErrorDetail>? Details = null);
