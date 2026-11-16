using FinMate.Application.Budgets;

namespace FinMate.Application.Common.Interfaces;

public interface IBudgetPeriodService
{
    /// <summary>
    /// Cộng dồn (delta dương) hoặc hoàn lại (delta âm) chi tiêu vào budget period chứa
    /// <paramref name="transactedAt"/>, cho mọi budget khớp category — kể cả budget tổng.
    /// Chỉ track thay đổi, KHÔNG gọi SaveChangesAsync: caller kết thúc bằng đúng 1 lần
    /// SaveChangesAsync nên transaction + balance + budget flush atomically.
    ///
    /// Trả về các cảnh báo ngưỡng vừa chạm (docx Flow 2 mục 2a — kiểm tra ngay khi giao dịch
    /// phát sinh). Caller phải gửi chúng SAU khi lưu thành công, không phải ở đây: hàm này cố
    /// ý không save, nên gửi push trong đây là báo cho người dùng về một giao dịch có thể
    /// chưa tồn tại nếu lần save sau đó thất bại.
    /// </summary>
    Task<IReadOnlyList<BudgetAlert>> ApplyDeltaAsync(
        Guid userId,
        Guid? categoryId,
        long spentDeltaCents,
        DateTimeOffset transactedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Tính LẠI <c>spent_cents</c> của mọi kỳ ngân sách bị chạm, thay vì cộng delta (Phase 81, D2). Dùng cho mọi thao tác có dính hoàn tiền
    /// hoặc đảo giao dịch: cộng delta rồi chặn ở 0 làm số lệch (hoàn ở tháng chưa chi → bị chặn, xoá khoản hoàn sau đó → cộng lại số không
    /// có thật). Số mới = max(0, chi ròng trong DB không kể <paramref name="pending"/> + phần của <paramref name="pending"/> theo trạng thái
    /// trong bộ nhớ), cùng một định nghĩa với backfill (<see cref="Budgets.BudgetSpendScope"/>).
    /// Cảnh báo chỉ đánh giá khi số mới LỚN HƠN số cũ, và không bao giờ reset cờ <c>alert_*_sent_at</c> (BR-RF-05). Không lưu.
    /// </summary>
    /// <param name="pending">Các giao dịch đang sửa trong bộ nhớ (thêm mới, đổi trạng thái, đổi liên kết, xoá mềm), đã nạp danh mục.</param>
    /// <param name="touched">Các cặp (danh mục, thời điểm) có thể đã đổi: cả giá trị CŨ lẫn MỚI của mỗi giao dịch trong <paramref name="pending"/>.</param>
    Task<IReadOnlyList<BudgetAlert>> RecomputeAsync(
        Guid userId,
        IReadOnlyCollection<Domain.Entities.Transaction> pending,
        IReadOnlyCollection<(Guid? CategoryId, DateTimeOffset At)> touched,
        CancellationToken ct = default);
}
