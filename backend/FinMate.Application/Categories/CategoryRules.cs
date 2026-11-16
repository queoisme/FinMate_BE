using System.Text.RegularExpressions;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentValidation;

namespace FinMate.Application.Categories;

/// <summary>Luật chung cho tạo và sửa danh mục riêng, để hai đường không lệch nhau.</summary>
public static class CategoryRules
{
    public static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>Trần hạn mức gợi ý: 100 tỷ đồng, cùng trần với các số tiền khác của backend.</summary>
    public const long MaxSuggestedBudgetCents = 100_000_000_000L;

    public static string? NormalizeColor(string? color) => string.IsNullOrWhiteSpace(color) ? null : color.Trim().ToUpperInvariant();

    public static IRuleBuilderOptions<T, string?> ColorRule<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(c => string.IsNullOrWhiteSpace(c) || HexColor.IsMatch(c.Trim())).WithMessage("Màu phải có dạng #RRGGBB.");

    public static IRuleBuilderOptions<T, string?> KindRule<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(k => k is null || k is Category.KindExpense or Category.KindIncome).WithMessage("Loại danh mục phải là expense hoặc income.");

    public static IRuleBuilderOptions<T, long?> SuggestedBudgetRule<T>(this IRuleBuilder<T, long?> rule)
        => rule.Must(v => v is null || (v >= 0 && v <= MaxSuggestedBudgetCents)).WithMessage("Hạn mức gợi ý phải từ 0 đến 100 tỷ đồng.");

    /// <summary>Nhóm cha phải là một danh mục HỆ THỐNG đang dùng: không lồng danh mục riêng, không dùng danh mục người khác hay danh mục đã tắt.</summary>
    public static async Task<Guid?> ResolveParentAsync(ICategoryRepository repository, Guid? parentId, CancellationToken ct)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await repository.GetByIdAsync(parentId.Value, ct);
        if (parent is null || !parent.IsSystem || parent.UserId is not null || !parent.IsActive)
        {
            throw new BusinessRuleException(CategoryErrorCodes.ParentInvalid, "Nhóm danh mục cha phải là một danh mục hệ thống đang dùng.");
        }

        return parent.Id;
    }
}
