using FinMate.Application.Transactions.Refunds;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;

namespace FinMate.Application.Catalog;

/// <summary>
/// Phase 95 (S4): danh mục hệ thống mà code gọi theo slug. <c>CategoryRepository.GetSystemBySlugAsync</c> chỉ trả danh mục ĐANG BẬT, nên tắt một trong số này làm
/// các luồng dưới rơi về null hoặc hỏng. Kênh <c>/ops/catalog</c> không được tắt chúng (422 <see cref="ErrorCode"/>); đổi tên vẫn được.
/// <list type="bullet">
/// <item><c>income</c>: khoản hoàn (<see cref="RefundPolicy"/>), lãi khi cho vay, luật quỹ nhóm, đồng bộ AI (kind), mẫu huấn luyện, bộ nhớ hoá đơn.</item>
/// <item><c>loan</c>: dòng tiền vay/trả nợ (<c>LoanExclusion</c>, <c>cashFlow</c>), gộp giao dịch, bộ nhớ tên món, chia hoá đơn nhóm.</item>
/// <item><c>other</c>: phí chuyển ví (<see cref="TransferFee"/>), lãi khi đi vay, danh mục dự phòng khi người dùng ẩn danh mục.</item>
/// </list>
/// Chuyển khoản, quỹ nhóm và hoàn tiền không có slug riêng (Transfer / relation), nên không nằm ở đây.
/// </summary>
public static class ProtectedCategories
{
    public const string ErrorCode = "PROTECTED_CATEGORY";

    public static readonly IReadOnlySet<string> Slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RefundPolicy.IncomeSlug,
        Category.LoanSlug,
        TransferFee.CategorySlug,
    };

    public static bool IsProtected(string slug) => Slugs.Contains(slug);
}
