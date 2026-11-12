using System.Linq.Expressions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Budgets;

/// <summary>
/// MỘT định nghĩa duy nhất cho "giao dịch nào tính vào <c>spent_cents</c> của một ngân sách, với dấu nào" (Phase 81, D2).
/// Dùng chung cho: backfill khi tạo ngân sách (FR-BUD-01), tính lại kỳ khi có hoàn/đảo, tổng chi đã chia sẻ của nhóm, và tính lại nhóm khi
/// xoá cứng tài khoản (FR-GRP-16). Viết dạng <see cref="Expression{TDelegate}"/> để EF dịch sang SQL, và <c>Compile()</c> được để xét các
/// giao dịch đang sửa trong bộ nhớ (chưa lưu) theo ĐÚNG cùng điều kiện — hai bản viết tay sẽ lệch nhau.
/// </summary>
public static class BudgetSpendScope
{
    /// <summary>
    /// Có tham gia tổng chi không: đã xác nhận, không phải cho vay/mượn, và là khoản chi chưa bị đảo hoặc một khoản hoàn. Đảo (reversal)
    /// không có tác động riêng; khoản gốc của nó đã bị loại nhờ <c>IsReversed</c>, nên cả cặp ròng bằng 0 (BR-RF-08).
    /// </summary>
    public static readonly Expression<Func<Transaction, bool>> Counts = t =>
        t.Status == TransactionStatus.Confirmed
        && (t.Category == null || t.Category.Slug != Category.LoanSlug)
        && ((t.TransactionType == TransactionType.Debit && !t.IsReversed)
            || (t.TransactionType == TransactionType.Credit && t.Relation == TransactionRelation.Refund));

    /// <summary>Số có dấu: chi +, hoàn −. Chỉ có nghĩa với giao dịch đã qua <see cref="Counts"/>.</summary>
    public static readonly Expression<Func<Transaction, long>> Signed = t =>
        t.TransactionType == TransactionType.Debit ? t.AmountCents : -t.AmountCents;

    private static readonly Func<Transaction, bool> CountsCompiled = Counts.Compile();
    private static readonly Func<Transaction, long> SignedCompiled = Signed.Compile();

    /// <summary>Ngân sách cá nhân: mọi giao dịch của user (kể cả đã chia sẻ vào nhóm). <paramref name="categoryId"/> null = ngân sách tổng.</summary>
    public static Expression<Func<Transaction, bool>> Personal(Guid userId, Guid? categoryId, DateTimeOffset from, DateTimeOffset to)
        => categoryId is { } id
            ? t => t.UserId == userId && t.CategoryId == id && t.TransactedAt >= from && t.TransactedAt < to
            : t => t.UserId == userId && t.TransactedAt >= from && t.TransactedAt < to;

    /// <summary>
    /// Ngân sách nhóm: khoản đã chia sẻ vào nhóm. Danh mục nhóm thắng danh mục hệ thống; ngân sách theo danh mục hệ thống chỉ đếm khoản
    /// chưa được phân loại lại bằng danh mục nhóm. Cả hai null = ngân sách tổng của nhóm.
    /// </summary>
    public static Expression<Func<Transaction, bool>> Group(Guid groupId, Guid? systemCategoryId, Guid? groupCategoryId, DateTimeOffset from, DateTimeOffset to)
    {
        if (groupCategoryId is { } groupCategory)
        {
            return t => t.GroupId == groupId && t.GroupCategoryId == groupCategory && t.TransactedAt >= from && t.TransactedAt < to;
        }

        if (systemCategoryId is { } systemCategory)
        {
            return t => t.GroupId == groupId && t.GroupCategoryId == null && t.CategoryId == systemCategory && t.TransactedAt >= from && t.TransactedAt < to;
        }

        return t => t.GroupId == groupId && t.TransactedAt >= from && t.TransactedAt < to;
    }

    /// <summary>Phần đóng góp có dấu của MỘT giao dịch (đang ở trong bộ nhớ) vào một phạm vi; 0 nếu nó không thuộc phạm vi hoặc không được tính.</summary>
    public static long ContributionOf(Transaction transaction, Expression<Func<Transaction, bool>> scope)
        => transaction.DeletedAt is null && CountsCompiled(transaction) && scope.Compile()(transaction) ? SignedCompiled(transaction) : 0;
}
