namespace FinMate.Domain.Enums;

/// <summary>Phản hồi của user cho một khoản bất thường (Flow 6, trang 9).</summary>
public enum AnomalyResolution
{
    /// <summary>Bình thường — ghi nhận để không báo khoản tương tự nữa (BR-11.4).</summary>
    Normal,

    /// <summary>Khoản chi một lần — giao dịch bị loại khỏi dữ liệu dự báo.</summary>
    OneOff,

    /// <summary>Nhập sai — user tự sửa hoặc xóa giao dịch.</summary>
    WrongEntry,

    /// <summary>Giao dịch lạ — app hiện hướng dẫn liên hệ ngân hàng.</summary>
    Suspicious,
}
