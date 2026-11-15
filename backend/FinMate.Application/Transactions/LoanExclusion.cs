using System.Linq.Expressions;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

/// <summary>
/// MỘT chỗ duy nhất quyết định "khoản này là cho vay/mượn" (danh mục <see cref="Category.LoanSlug"/>): tiền cho mượn sẽ về lại, tiền mượn sẽ
/// phải trả, nên không bao giờ là chi tiêu hay thu nhập. Trước Phase 85 điều kiện này được viết tay ở 12 nơi và một nơi bị sót
/// (<c>FinanceSnapshotRepository</c>, làm <c>/me/spendable</c> trừ cả khoản cho mượn).
/// </summary>
public static class LoanExclusion
{
    /// <summary>Cho truy vấn EF: giao dịch KHÔNG thuộc cho vay/mượn. Giao dịch không có danh mục vẫn được giữ.</summary>
    public static readonly Expression<Func<Transaction, bool>> NotLoan = t => t.Category == null || t.Category.Slug != Category.LoanSlug;

    /// <summary>Cho dữ liệu trong bộ nhớ. <paramref name="category"/> null nghĩa là chưa phân loại, không phải cho vay.</summary>
    public static bool IsLoan(Category? category) => category?.Slug == Category.LoanSlug;
}
