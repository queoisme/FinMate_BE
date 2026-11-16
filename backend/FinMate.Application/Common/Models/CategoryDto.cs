using FinMate.Application.Admin.SystemConfig;
using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Models;

/// <param name="Color"><c>#RRGGBB</c> chữ hoa hoặc null (app dùng màu mặc định).</param>
/// <param name="Kind"><c>expense</c> hoặc <c>income</c>.</param>
/// <param name="ParentCategoryId">Danh mục hệ thống làm nhóm cha, hoặc null.</param>
/// <param name="SuggestedBudgetCents">Hạn mức ngân sách gợi ý mỗi tháng (đồng); chỉ để hiển thị, không phải ngân sách.</param>
public record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    string? IconName,
    bool IsSystem,
    DateTimeOffset CreatedAt,
    string? Color = null,
    string Kind = Category.KindExpense,
    Guid? ParentCategoryId = null,
    long? SuggestedBudgetCents = null,
    bool IsFavorite = false,
    bool IsHidden = false)
{
    public static CategoryDto From(Category c) => new(
        c.Id, c.Name, c.Slug, c.IconName, c.IsSystem, c.CreatedAt, c.Color, c.Kind, c.ParentCategoryId, c.SuggestedBudgetCents, c.IsFavorite);

    public CategoryDto WithHidden(bool hidden) => this with { IsHidden = hidden };
}

/// <summary>Lựa chọn cho màn thêm/sửa danh mục: bảng màu lấy từ cấu hình hệ thống (admin đổi được) và các loại danh mục.</summary>
public record CategoryOptionsDto(IReadOnlyList<CategoryColor> Palette, IReadOnlyList<string> Kinds);
