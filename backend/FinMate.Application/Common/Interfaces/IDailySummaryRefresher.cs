namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Chốt lại <c>daily_summaries</c> của những ngày ĐÃ chốt mà một thao tác hoàn/đảo vừa làm đổi số (Phase 81, D7). Gọi SAU khi lưu; tự nuốt
/// lỗi (bảng tóm tắt là bản đệm, sai một ngày thì job đêm hay lần chốt sau sửa được, không được làm hỏng thao tác tiền đã lưu xong).
/// Ngày chưa chốt (hôm nay) thì bỏ qua: báo cáo tháng đọc phần đó trực tiếp.
/// </summary>
public interface IDailySummaryRefresher
{
    Task RefreshAsync(Guid userId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default);
}
