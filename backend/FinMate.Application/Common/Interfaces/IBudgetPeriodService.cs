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
}
