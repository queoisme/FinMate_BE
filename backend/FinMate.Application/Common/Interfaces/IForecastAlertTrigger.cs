namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Báo rằng dữ liệu tiền của user vừa đổi (giao dịch tạo/xác nhận/sửa/xóa), để kiểm tra số dư thấp và vượt ngân sách sớm ngoài request
/// (sơ đồ trang 1 và 6: "Có giao dịch mới"). Không bao giờ ném lỗi: cảnh báo không được làm hỏng việc lưu giao dịch.
/// </summary>
public interface IForecastAlertTrigger
{
    Task UserChangedAsync(Guid userId, CancellationToken ct = default);
}
