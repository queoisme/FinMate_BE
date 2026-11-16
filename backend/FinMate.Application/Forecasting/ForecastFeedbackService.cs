using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;

namespace FinMate.Application.Forecasting;

public static class ForecastFeedbackErrorCodes
{
    public const string Invalid = "FORECAST_FEEDBACK_INVALID";
}

public sealed record SubmitForecastFeedbackCommand(Guid UserId, string Reason, string? Note);

public sealed record SubmittedForecastFeedbackDto(Guid Id, string Status);

/// <summary>Con số của bản chụp mà phản hồi gắn vào. Staff thấy số dư dự báo và thực tế chứ không thấy giao dịch hay danh tính người dùng.</summary>
public sealed record ForecastFeedbackSnapshotDto(
    DateOnly OriginDate, int HorizonDays, long PredictedCents, long ConservativeCents, long OptimisticCents, long? ActualCents, string Confidence, string Method);

public sealed record ForecastFeedbackDto(
    Guid Id, DateTimeOffset CreatedAt, string Reason, string? Note, string Status, string? Cause, ForecastFeedbackSnapshotDto? Snapshot);

public sealed record ForecastFeedbackPageDto(IReadOnlyList<ForecastFeedbackDto> Items, int Total);

public interface IForecastFeedbackService
{
    /// <summary>Người dùng báo dự báo sai. Gắn với bản chụp gần nhất của họ; gửi lại khi phản hồi trước chưa được xử lý thì trả lại phản hồi đó.</summary>
    Task<SubmittedForecastFeedbackDto> SubmitAsync(SubmitForecastFeedbackCommand command, CancellationToken ct = default);

    /// <summary>Hàng đợi cho Staff (quyền xử lý ticket). Không có định danh người dùng.</summary>
    Task<ForecastFeedbackPageDto> ListAsync(Guid actorId, string? status, int limit, int offset, CancellationToken ct = default);

    /// <summary>Staff gán nguyên nhân. Gán lại được; mỗi lần ghi nhật ký nghiệp vụ.</summary>
    Task<ForecastFeedbackDto> LabelAsync(Guid actorId, Guid feedbackId, string cause, string? ip, CancellationToken ct = default);
}

public class ForecastFeedbackService(IForecastAccuracyRepository repository, IOpsAccessService access, IAuditLogService audit) : IForecastFeedbackService
{
    public const int NoteMaxLength = 300;
    private const int MaxPageSize = 100;

    public async Task<SubmittedForecastFeedbackDto> SubmitAsync(SubmitForecastFeedbackCommand command, CancellationToken ct = default)
    {
        if (!ForecastFeedbackReasons.All.Contains(command.Reason))
        {
            throw new BusinessRuleException(ForecastFeedbackErrorCodes.Invalid, $"Lý do phải là một trong: {string.Join(", ", ForecastFeedbackReasons.All)}.");
        }

        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > NoteMaxLength })
        {
            throw new BusinessRuleException(ForecastFeedbackErrorCodes.Invalid, $"Lời nhắn tối đa {NoteMaxLength} ký tự.");
        }

        var snapshot = await repository.GetLatestForUserAsync(command.UserId, ct);
        if (snapshot is not null && await repository.GetNewFeedbackForSnapshotAsync(command.UserId, snapshot.Id, ct) is { } existing)
        {
            return new SubmittedForecastFeedbackDto(existing.Id, existing.Status);
        }

        var feedback = new ForecastFeedback
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            SnapshotId = snapshot?.Id,
            Reason = command.Reason,
            Note = note,
            Status = ForecastFeedbackStatuses.New,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await repository.AddFeedbackAsync(feedback, ct);
        await repository.SaveChangesAsync(ct);
        return new SubmittedForecastFeedbackDto(feedback.Id, feedback.Status);
    }

    public async Task<ForecastFeedbackPageDto> ListAsync(Guid actorId, string? status, int limit, int offset, CancellationToken ct = default)
    {
        await access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        if (status is not null && status != ForecastFeedbackStatuses.New && status != ForecastFeedbackStatuses.Labeled)
        {
            throw new BusinessRuleException(ForecastFeedbackErrorCodes.Invalid, "Trạng thái phải là new hoặc labeled.");
        }

        var (items, total) = await repository.ListFeedbackAsync(status, Math.Clamp(limit, 1, MaxPageSize), Math.Max(offset, 0), ct);
        return new ForecastFeedbackPageDto(items.Select(ToDto).ToList(), total);
    }

    public async Task<ForecastFeedbackDto> LabelAsync(Guid actorId, Guid feedbackId, string cause, string? ip, CancellationToken ct = default)
    {
        var actor = await access.RequireAsync(actorId, OpsCapability.HandleTickets, ct);
        if (!ForecastFeedbackCauses.All.Contains(cause))
        {
            throw new BusinessRuleException(ForecastFeedbackErrorCodes.Invalid, $"Nguyên nhân phải là một trong: {string.Join(", ", ForecastFeedbackCauses.All)}.");
        }

        var feedback = await repository.GetFeedbackAsync(feedbackId, ct) ?? throw new NotFoundException("ForecastFeedback", feedbackId);
        feedback.Cause = cause;
        feedback.Status = ForecastFeedbackStatuses.Labeled;
        feedback.LabeledByUserId = actor.Id;
        feedback.LabeledAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(ct);

        // Chỉ ghi nguyên nhân và id phản hồi: không có định danh người dùng hay lời nhắn của họ.
        await audit.LogAsync(AuditEvents.OpsForecastFeedbackLabeled, actor.Id, ip, new { FeedbackId = feedback.Id, Cause = cause }, ct);
        return ToDto(feedback);
    }

    private static ForecastFeedbackDto ToDto(ForecastFeedback f)
        => new(
            f.Id, f.CreatedAt, f.Reason, f.Note, f.Status, f.Cause,
            f.Snapshot is null
                ? null
                : new ForecastFeedbackSnapshotDto(
                    f.Snapshot.OriginDate, f.Snapshot.HorizonDays, f.Snapshot.PredictedCents, f.Snapshot.ConservativeCents, f.Snapshot.OptimisticCents,
                    f.Snapshot.ActualCents, f.Snapshot.Confidence, f.Snapshot.Method));
}
