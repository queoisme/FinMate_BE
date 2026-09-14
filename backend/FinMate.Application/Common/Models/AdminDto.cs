using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Models;

/// <summary>
/// Người dùng nhìn từ màn hình quản trị.
///
/// KHÔNG có trường tài chính nào — không số dư, không giao dịch, không số tiền. Đây là ràng
/// buộc của ARCHITECTURE.md §7.3 ("Admin không đọc được amount_cents hay description của user
/// cụ thể"), và nó được giữ bằng cách DTO này đơn giản là không có chỗ để đặt chúng.
/// </summary>
public record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role,
    bool IsLocked,
    bool HasPassword,
    bool HasGoogleLink,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public record AdminUserListDto(IReadOnlyList<AdminUserDto> Items, string? NextCursor);

/// <param name="ProcessedAt">
/// Lúc yêu cầu thôi ở trạng thái chờ — <paramref name="Status"/> nói theo hướng nào.
/// </param>
public record DataDeletionRequestDto(
    Guid Id,
    Guid UserId,
    string Email,
    string DisplayName,
    DataDeletionStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset ScheduledHardDeleteAt,
    DateTimeOffset? ProcessedAt,
    int DaysUntilHardDelete);

public record DataDeletionRequestListDto(
    IReadOnlyList<DataDeletionRequestDto> Items, string? NextCursor);

public record ProviderConfigDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string PackageName,
    AccountType AccountType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? IconName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record AdminMissionDto(
    Guid Id,
    string Code,
    string Title,
    string Description,
    MissionPeriodType PeriodType,
    MissionConditionType ConditionType,
    int ConditionTarget,
    int ExpReward,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <param name="Metadata">
/// Chuỗi JSON thô do handler ghi ra, mỗi loại sự kiện một hình dạng khác nhau. Trả nguyên văn
/// chứ không parse: không có schema chung để parse theo, và cố ép một schema vào đây sẽ làm
/// hỏng đúng những dòng bất thường mà người xem log đang đi tìm.
/// </param>
public record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string EventType,
    string? Metadata,
    string? IpAddress,
    DateTimeOffset CreatedAt);

public record AuditLogListDto(IReadOnlyList<AuditLogDto> Items, string? NextCursor);

/// <param name="CategoryCorrectionRate">
/// null khi chưa có giao dịch nào đủ điều kiện để tính — KHÁC với 0 (AI đoán đúng hết). Ép về
/// 0 sẽ làm dashboard của một hệ thống chưa có dữ liệu trông như một hệ thống hoàn hảo.
/// </param>
public record AiProductionStatsDto(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalAnalyzed,
    IReadOnlyDictionary<string, int> ByPipelineResult,
    IReadOnlyDictionary<string, int> ByPackageName,
    IReadOnlyDictionary<string, int> ByClassifierVersion,
    int PotentialDuplicates,
    int DraftsCreated,
    int DraftsConfirmed,
    double? DraftConfirmRate,
    int CategoryCorrections,
    double? CategoryCorrectionRate,
    double? AvgClassifierConfidence,
    double? AvgExtractionConfidence,
    double? AvgCategorizationConfidence,
    double? AvgProcessingMs,
    int? MaxProcessingMs);

public record AiModelStatusDto(
    string Stage,
    string? Version,
    DateTimeOffset? TrainedAt,
    double? Accuracy,
    double? MacroF1,
    string? EvaluatedOnSplit);

public record AiTrainingJobDto(
    string Stage,
    string Status,
    int? SampleCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrorMessage);

public record AiServiceStatsDto(
    IReadOnlyList<AiModelStatusDto> Models,
    int RawSampleCount,
    int LabeledSampleCount,
    int UnlabeledSampleCount,
    IReadOnlyDictionary<string, int> SplitCounts,
    AiTrainingJobDto? LastTrainingJob,
    int PendingFeedbackCount);

/// <param name="AiService">
/// null khi AI Service không phản hồi — <paramref name="AiServiceError"/> nói lý do. Phần
/// <paramref name="Production"/> luôn có, vì nó nằm trong backend DB.
/// </param>
public record AiStatsDto(
    AiProductionStatsDto Production,
    AiServiceStatsDto? AiService,
    string? AiServiceError);
