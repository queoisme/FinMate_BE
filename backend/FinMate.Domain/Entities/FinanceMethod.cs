using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>
/// Một cách quản lý tài chính app hỗ trợ — chính là danh sách mà AI chỉ được chọn trong đó.
///
/// Admin sửa nội dung, tham số, điều kiện và bật/tắt; KHÔNG tạo được cơ chế mới (mỗi
/// <see cref="Code"/> ứng với một nhánh code). <see cref="Code"/> bất biến sau khi tạo vì
/// <c>user_methods</c> và prompt AI nối theo nó. Không xóa cứng: tắt bằng <see cref="IsActive"/>.
/// </summary>
public class FinanceMethod
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public FinanceMethodRole Role { get; set; } = FinanceMethodRole.Primary;
    public GoalMode GoalMode { get; set; } = GoalMode.TrackOnly;

    /// <summary>JSON array: [{key, label, type, default, min, max, userEditable}] — T1, T2, giờ nhắc… Giá trị số nguyên (phần trăm, đồng, phút), không float. Kiểu: <see cref="FinanceParamTypes"/>.</summary>
    public string ParamDefs { get; set; } = "[]";

    /// <summary>
    /// JSON object: {weights: {signal: trọng số}, excludes: [signal]}. <c>weights</c> CỘNG ĐIỂM cho phương pháp khi người dùng có signal đó
    /// (không phải điều kiện bắt buộc); <c>excludes</c> LOẠI HẲN phương pháp khỏi shortlist. Signal: <see cref="FinanceSignals"/>.
    /// </summary>
    public string Eligibility { get; set; } = "{}";

    /// <summary>Lý do hiển thị cho người dùng khi phương pháp được gợi ý.</summary>
    public string WhyText { get; set; } = string.Empty;

    /// <summary>JSON array các bước bắt đầu (3 bước).</summary>
    public string StarterSteps { get; set; } = "[]";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Tăng mỗi lần admin đổi nội dung/cơ chế; ghi kèm gợi ý và lựa chọn của người dùng để truy ngược.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
