namespace FinMate.Application.Admin.Commands;

/// <param name="AdminId">Lấy từ JWT claims, không bao giờ từ body — CONVENTIONS.md §6.2.</param>
public record CancelDataDeletionRequestCommand(
    Guid AdminId,
    Guid RequestId,
    string? Reason,
    string? IpAddress);
