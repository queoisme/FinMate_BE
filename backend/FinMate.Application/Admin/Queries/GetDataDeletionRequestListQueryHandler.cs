using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Queries;

public class GetDataDeletionRequestListQueryHandler : IGetDataDeletionRequestListQueryHandler
{
    private readonly IDataDeletionRequestRepository _repository;

    public GetDataDeletionRequestListQueryHandler(IDataDeletionRequestRepository repository)
    {
        _repository = repository;
    }

    public async Task<DataDeletionRequestListDto> HandleAsync(
        GetDataDeletionRequestListQuery query, CancellationToken ct = default)
    {
        var result = await _repository.GetPageAsync(
            new DataDeletionRequestListFilter(query.Status, query.Cursor, AdminPaging.Clamp(query.Limit)),
            ct);

        var now = DateTimeOffset.UtcNow;

        return new DataDeletionRequestListDto(
            result.Items.Select(row => ToDto(row, now)).ToList(),
            result.NextCursor);
    }

    private static DataDeletionRequestDto ToDto(DataDeletionRequestRow row, DateTimeOffset now)
    {
        var request = row.Request;

        // Tính sẵn số ngày còn lại thay vì để client tự trừ: đây là con số quyết định admin có
        // phải xử lý gấp hay không, và mỗi client tự tính là mỗi nơi làm tròn một kiểu.
        // Chỉ có nghĩa khi còn đang chờ — yêu cầu đã xong hoặc đã huỷ thì không đếm ngược nữa.
        var daysLeft = request.Status == DataDeletionStatus.Pending
            ? Math.Max(0, (int)Math.Ceiling((request.ScheduledHardDeleteAt - now).TotalDays))
            : 0;

        return new DataDeletionRequestDto(
            request.Id,
            request.UserId,
            row.Email,
            row.DisplayName,
            request.Status,
            request.RequestedAt,
            request.ScheduledHardDeleteAt,
            request.ProcessedAt,
            daysLeft);
    }
}
