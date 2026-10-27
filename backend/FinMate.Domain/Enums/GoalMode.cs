namespace FinMate.Domain.Enums;

/// <summary>
/// Cách khoản để dành cho mục tiêu đi vào dòng tiền của một phương pháp. Cố ý là enum chứ không
/// phải chuỗi tự do: mỗi giá trị là một nhánh code tính "tiền khả dụng", admin không tự thêm
/// được cơ chế mới.
/// </summary>
public enum GoalMode
{
    /// <summary>Trừ ngay khi có thu nhập ("trả cho mình trước").</summary>
    ReserveFirst,

    /// <summary>Mục tiêu là một phong bì riêng.</summary>
    Envelope,

    /// <summary>Khoản nạp trừ khỏi số tiền còn được chi.</summary>
    AvailableToSpend,

    /// <summary>Chỉ hiển thị tiến độ, không tự trừ gì.</summary>
    TrackOnly,
}
