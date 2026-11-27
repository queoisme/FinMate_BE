namespace FinMate.Application.ForecastConfig;

/// <summary>Hai nhóm đang chạy, phiên bản và bản đang chờ duyệt (nếu có).</summary>
public sealed record ForecastConfigCurrentDto(int Version, ForecastConfigSections Sections, Guid? PendingId);

public sealed record ForecastConfigValidationError(string Field, string Message);

/// <summary>
/// Một bản nháp. <see cref="Diff"/>: với bản còn mở (nháp, chờ duyệt) so với bản ĐANG CHẠY; với bản đã xử lý so với phiên bản gốc của nó.
/// <see cref="Stale"/>: phiên bản gốc không còn là phiên bản đang chạy (không duyệt được, cần "làm lại").
/// </summary>
public sealed record ForecastConfigDraftDto(
    Guid Id,
    string Status,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int BaseVersion,
    int? ApprovedVersion,
    DateTimeOffset? SubmittedAt,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    bool Stale,
    ForecastConfigSections Sections,
    IReadOnlyList<ForecastConfigChange> Diff,
    IReadOnlyList<ForecastConfigConflict> Conflicts,
    IReadOnlyList<ForecastConfigValidationError> Validation);

/// <summary>Tạo bản nháp: <c>changes</c> (phần thay đổi), tuỳ chọn bắt đầu từ một bản nháp khác (<c>fromDraftId</c>, "làm lại") hoặc một phiên bản lịch sử (<c>fromVersion</c>, hoàn tác).</summary>
public sealed record CreateForecastConfigDraft(System.Text.Json.JsonElement? Changes, Guid? FromDraftId, int? FromVersion, string? Reason);

public sealed record UpdateForecastConfigDraft(System.Text.Json.JsonElement Changes, string? Reason, DateTimeOffset? ExpectedUpdatedAt);
