namespace FinMate.Application.Transactions;

/// <summary>
/// Chuẩn hoá tên merchant thành khóa tra cứu cho <c>MerchantCategoryOverride</c> (§16.5). Ghi và
/// đọc PHẢI dùng chung hàm này, không lệch.
/// </summary>
public static class MerchantCategoryKey
{
    public static string Normalize(string merchantName) => merchantName.Trim().ToLowerInvariant();
}
