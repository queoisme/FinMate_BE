using System.Text.RegularExpressions;

namespace FinMate.Application.Agent;

/// <summary>
/// Câu chữ cố định mà công cụ của agent ghép số vào (mẫu có chỗ trống <c>{tên}</c>). Admin sửa trong cấu hình hệ thống (<c>agent.texts</c>, có phiên bản);
/// bảng dưới đây chỉ là giá trị rơi về khi admin chưa sửa hoặc mẫu admin nhập sai chỗ trống. Con số vẫn do code tính, mẫu chỉ quyết định cách nói.
/// </summary>
public static partial class AgentTexts
{
    /// <summary>Khóa → (mẫu mặc định, các chỗ trống được phép dùng).</summary>
    public static readonly IReadOnlyDictionary<string, (string Template, string[] Slots)> Catalog = new Dictionary<string, (string, string[])>
    {
        ["goal_plan_question"] = ("Mục tiêu tiết kiệm {target} đồng trong {months} tháng có khả thi không, mỗi tháng cần góp bao nhiêu?", ["target", "months"]),
        ["duration_years"] = ("{years} năm", ["years"]),
        ["duration_months"] = ("{months} tháng", ["months"]),
        ["duration_years_months"] = ("{years} năm {months} tháng", ["years", "months"]),
        ["goal_target"] = ("Số tiền mục tiêu tiết kiệm do người dùng nêu, trong {months} tháng ({duration})", ["months", "duration"]),
        ["plan_required"] = ("Cần góp mỗi tháng để đủ mục tiêu trong {months} tháng ({duration})", ["months", "duration"]),
        ["plan_required_share"] = ("Cần góp mỗi tháng để đủ mục tiêu trong {months} tháng ({duration}), khoảng {share}% thu nhập bạn khai", ["months", "duration", "share"]),
        ["capacity_base"] = ("Khả năng góp mỗi tháng điển hình (thu trung vị trừ chi trung vị)", []),
        ["capacity_conservative"] = ("Khả năng góp mỗi tháng khi thu thấp và chi cao (thu phân vị 10 trừ chi phân vị 90)", []),
        ["income_declared"] = ("Thu nhập hằng tháng bạn đã khai trong hồ sơ", []),
        ["spend_avg"] = ("Chi tiêu trung bình mỗi tháng theo {count} tháng đã ghi", ["count"]),
        ["capacity_from_spend"] = ("Khả năng góp mỗi tháng ước tính (thu nhập đã khai trừ chi tiêu trung bình)", []),
        ["capacity_from_method"] = ("Khả năng góp mỗi tháng ước tính theo cách quản lý {method}: dành khoảng {percent}% thu nhập bạn khai", ["method", "percent"]),
        ["alt_monthly"] = ("Nếu kéo dài lên {months} tháng ({duration}) thì cần góp mỗi tháng", ["months", "duration"]),
        ["action_create_goal"] = ("Tạo mục tiêu {duration} · góp {monthly}/tháng", ["duration", "monthly"]),
        ["action_change_deadline"] = ("Kéo dài thành {duration} · góp {monthly}/tháng", ["duration", "monthly"]),
        ["cut_saving"] = ("Nếu giảm {percent}% chi tiêu nhóm {category} thì mỗi tháng dành thêm được", ["percent", "category"]),
        ["cut_horizon"] = ("Giảm {percent}% chi nhóm {category} thì đủ mục tiêu sau khoảng {duration}, mỗi tháng góp", ["percent", "category", "duration"]),
        ["action_cut_limit"] = ("Đặt hạn mức {category} {limit}/tháng · đủ mục tiêu sau {duration}", ["category", "limit", "duration"]),
        ["capacity_from_survey"] = ("Khả năng góp mỗi tháng ước tính theo câu trả lời khảo sát ban đầu của bạn: dành khoảng {percent}% thu nhập bạn khai", ["percent"]),
        ["basis_survey"] = (" và câu trả lời khảo sát ban đầu của bạn (dành khoảng {percent}% thu nhập để tiết kiệm)", ["percent"]),
        ["spend_declared"] = ("Mức chi trung bình mỗi tháng bạn chọn trong khảo sát ban đầu (ước lượng theo khoảng)", []),
        ["capacity_from_survey_spend"] = ("Khả năng góp mỗi tháng ước tính (thu nhập bạn khai trừ mức chi bạn chọn trong khảo sát)", []),
        ["basis_survey_spend"] = (" và mức chi bạn chọn trong khảo sát ban đầu", []),
        ["basis_spend"] = (" và chi tiêu trung bình của {count} tháng đã ghi", ["count"]),
        ["basis_method"] = (" và cách quản lý bạn đã chọn ({method}, dành khoảng {percent}% thu nhập để tiết kiệm)", ["method", "percent"]),
        ["preliminary_note"] = ("Chỉ để tham khảo: mới dựa trên thu nhập bạn khai{basis}. Ghi chép chi tiêu thật (≥ {minMonths} tháng) để mình tính chính xác hơn.", ["basis", "minMonths"]),
    };

    public const int MaxLength = 300;

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex SlotPattern();

    /// <summary>Lỗi của một mẫu admin nhập (khóa lạ, rỗng, quá dài, chỗ trống không được phép), hoặc null nếu hợp lệ.</summary>
    public static string? Problem(string key, string? template)
    {
        if (!Catalog.TryGetValue(key, out var spec))
        {
            return $"Mẫu câu '{key}' không tồn tại.";
        }

        if (string.IsNullOrWhiteSpace(template) || template.Length > MaxLength)
        {
            return $"Mẫu câu '{key}' phải có nội dung và tối đa {MaxLength} ký tự.";
        }

        var unknown = SlotPattern().Matches(template).Select(m => m.Groups[1].Value).FirstOrDefault(s => !spec.Slots.Contains(s));
        return unknown is null ? null : $"Mẫu câu '{key}' dùng chỗ trống {{{unknown}}} không được phép (được dùng: {(spec.Slots.Length == 0 ? "không có" : string.Join(", ", spec.Slots))}).";
    }

    /// <summary>Điền chỗ trống vào mẫu của admin; mẫu thiếu hoặc sai thì dùng mẫu mặc định của chính khóa đó.</summary>
    public static string Render(IReadOnlyDictionary<string, string>? overrides, string key, params (string Slot, object Value)[] values)
    {
        var template = overrides is not null && overrides.TryGetValue(key, out var custom) && Problem(key, custom) is null ? custom : Catalog[key].Template;
        foreach (var (slot, value) in values)
        {
            template = template.Replace("{" + slot + "}", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return template;
    }
}
