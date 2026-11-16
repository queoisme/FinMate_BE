using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

/// <summary>
/// Danh sách danh mục gửi cho ai-service khi tách hóa đơn theo món, để AI chọn được cả danh mục người dùng tự tạo.
/// Chỉ danh mục CHI TIÊU (không thu nhập, không cho vay). Người dùng không có danh mục riêng chi tiêu thì trả null:
/// đường cũ (chỉ slug hệ thống) đã đủ và ai-service không phải nhận danh sách thừa.
/// </summary>
public static class UserCategoryChoices
{
    /// <summary>Giới hạn của ai-service: 61 mục trở lên là HTTP 422 và cả lần tách mất, nên phải tự cắt ở đây.</summary>
    public const int MaxCategories = 60;

    /// <summary>
    /// <paramref name="visible"/> là danh sách <c>GetListForUserAsync</c> (hệ thống đang bật + của người dùng); danh mục người khác bị bỏ qua dù có lẫn vào.
    /// Thứ tự trả về: toàn bộ danh mục hệ thống, rồi danh mục riêng. Khi vượt trần: giữ HẾT danh mục hệ thống, phần chỗ còn lại cho danh mục riêng
    /// ưu tiên yêu thích rồi mới nhất. Nếu riêng danh mục hệ thống đã vượt 60 thì giữ 60 cái đầu theo slug và không còn chỗ cho danh mục riêng.
    /// <c>ParentSlug</c> luôn tra trong CHÍNH danh sách trả về, nên không bao giờ trỏ tới một slug không được gửi (cha thiếu thì null).
    /// </summary>
    public static IReadOnlyList<ClassifyCategoryInput>? Build(IReadOnlyList<Category> visible, Guid userId)
    {
        var own = visible.Where(c => c.UserId == userId && IsChoice(c)).ToList();
        if (own.Count == 0)
        {
            return null;
        }

        var system = visible.Where(c => c.UserId is null && IsChoice(c)).ToList();
        if (system.Count > MaxCategories)
        {
            system = system.OrderBy(c => c.Slug, StringComparer.Ordinal).Take(MaxCategories).ToList();
        }

        var kept = system
            .Concat(own.OrderByDescending(c => c.IsFavorite).ThenByDescending(c => c.CreatedAt).Take(MaxCategories - system.Count))
            .ToList();

        var slugById = new Dictionary<Guid, string>();
        foreach (var c in kept)
        {
            slugById.TryAdd(c.Id, c.Slug);
        }

        return kept
            .Select(c => new ClassifyCategoryInput(
                c.Id, c.Name, c.Slug, c.UserId is null,
                c.ParentCategoryId is { } parent && slugById.TryGetValue(parent, out var parentSlug) ? parentSlug : null))
            .ToList();
    }

    /// <summary>
    /// Slug mà ai-service nhận được ở các đường chỉ biết slug HỆ THỐNG (vd. mẫu huấn luyện <c>/bill/samples</c>): slug của danh mục riêng
    /// đổi thành slug nhóm cha hệ thống của nó, hoặc null khi không có cha; slug hệ thống (hay không tra được) giữ nguyên.
    /// </summary>
    public static string? SystemSlugOf(string? slug, IReadOnlyList<Category>? visible, Guid userId)
    {
        if (slug is null || visible is null)
        {
            return slug;
        }

        var own = visible.FirstOrDefault(c => c.UserId == userId && c.Slug == slug);
        if (own is null)
        {
            return slug;
        }

        return own.ParentCategoryId is { } parentId
            ? visible.FirstOrDefault(c => c.Id == parentId && c.UserId is null)?.Slug
            : null;
    }

    // Còn dùng được, có tên, là chi tiêu (thu nhập nhận ra cả bằng slug "income" vì danh mục hệ thống cũ chưa chắc có Kind đúng) và không phải cho vay.
    private static bool IsChoice(Category c)
        => c.IsActive && c.DeletedAt is null && !string.IsNullOrWhiteSpace(c.Name)
           && c.Kind == Category.KindExpense && c.Slug != Category.LoanSlug && c.Slug != TrainingSampleService.IncomeSlug;
}
