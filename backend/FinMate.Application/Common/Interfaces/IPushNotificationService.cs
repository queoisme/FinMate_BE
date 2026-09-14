namespace FinMate.Application.Common.Interfaces;

public interface IPushNotificationService
{
    /// <param name="data">
    /// Dữ liệu có cấu trúc đi kèm để client dựng được hành động, ví dụ nút "Xác nhận" một
    /// chạm của docx Flow 1 bước 5.2 — chỉ có chữ thì client biết có giao dịch mới nhưng
    /// không biết xác nhận CÁI NÀO.
    ///
    /// KHÔNG BAO GIỜ ghi log: nó mang số tiền, cùng luật với <c>notification_body</c>.
    /// </param>
    Task NotifyAsync(
        Guid userId,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default);
}
