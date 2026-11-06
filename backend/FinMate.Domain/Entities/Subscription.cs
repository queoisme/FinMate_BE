using FinMate.Domain.Enums;

namespace FinMate.Domain.Entities;

/// <summary>Một gói đăng ký do Manager cấu hình. Không xóa vật lý: đơn và kỳ đã bán giữ bản chụp riêng.</summary>
public class SubscriptionPlan
{
    public Guid Id { get; set; }

    /// <summary>Không đổi sau khi tạo, vì đơn và kỳ đã bán lưu lại mã này.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Đơn vị như mọi cột <c>*_cents</c>: đồng VND. Null nghĩa là gói không bán theo chu kỳ này.</summary>
    public long? MonthlyPriceCents { get; set; }
    public long? YearlyPriceCents { get; set; }

    /// <summary>Gói người chưa mua được dùng. Có đúng một gói như vậy, không bán và không xóa được.</summary>
    public bool IsDefaultFree { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<SubscriptionPlanFeature> Features { get; set; } = [];
}

public class SubscriptionPlanFeature
{
    public Guid PlanId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    /// <summary>Số lượt mỗi tháng. Null là không giới hạn.</summary>
    public int? MonthlyLimit { get; set; }
}

/// <summary>Một lần mua. Chụp lại giá và quyền lợi lúc tạo đơn, nên Manager sửa gói không làm đổi điều khách đã thấy. Không lưu thông tin thẻ.</summary>
public class SubscriptionOrder
{
    public Guid Id { get; set; }

    /// <summary>Mã gửi sang SePay. Không lộ id người dùng.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Null sau khi người dùng bị xóa cứng: sổ tiền vẫn được giữ, không còn nối với người.</summary>
    public Guid? UserId { get; set; }
    public Guid PlanId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "[]";
    public SubscriptionCycle Cycle { get; set; }
    public long AmountCents { get; set; }
    public SubscriptionOrderStatus Status { get; set; }
    public string? SepayTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Lần tới job đối soát hỏi SePay về đơn này. Giãn dần theo tuổi đơn, để đơn bỏ dở không chiếm lượt của đơn mới.</summary>
    public DateTimeOffset NextCheckAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }

    /// <summary>Mã lý do cố định, không chứa số tiền hay văn bản từ bên ngoài.</summary>
    public string? ReviewReason { get; set; }

    /// <summary>Đã mở sự cố cho đơn này. Mỗi đơn chỉ báo một lần, đóng sự cố rồi không bị mở lại.</summary>
    public DateTimeOffset? ReviewAlertedAt { get; set; }
}

/// <summary>Một khoảng thời gian người dùng có gói trả phí. Gia hạn và đổi gói đều là thêm một kỳ nối tiếp; gói hiện tại là kỳ chứa thời điểm đang xét.</summary>
public class UserSubscriptionPeriod
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string FeaturesJson { get; set; } = "[]";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public DateTimeOffset? Reminded7dAt { get; set; }
    public DateTimeOffset? Reminded1dAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SubscriptionUsage
{
    public Guid UserId { get; set; }
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>Ngày đầu tháng theo giờ Việt Nam.</summary>
    public DateOnly Month { get; set; }
    public int Used { get; set; }
}
