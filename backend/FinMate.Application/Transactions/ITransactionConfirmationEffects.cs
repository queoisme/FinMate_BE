using FinMate.Application.Budgets;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions;

/// <summary>
/// Side-effect của việc chốt một giao dịch thành Confirmed: cộng/trừ số dư ví, áp delta vào
/// ngân sách, xoá cache tổng hợp, gửi cảnh báo ngân sách và thông báo biến động số dư.
///
/// Dùng chung giữa <c>ConfirmTransactionCommandHandler</c> (user tự xác nhận) và nhánh
/// Auto-commit của <c>AnalyzeNotificationCommandHandler</c> (§7/§16.1) — 2 đường phải luôn cho
/// cùng một kết quả cuối, nên chỉ viết logic này một lần.
///
/// Tách làm 2 bước đúng theo thứ tự an toàn gốc: <see cref="ApplyAsync"/> chỉ MUTATE (không
/// SaveChanges, không gửi gì) để caller tự AddAsync/UpdateAsync 1 lần duy nhất; sau khi lưu
/// thành công, caller gọi <see cref="NotifyAsync"/> để xoá cache + gửi cảnh báo/thông báo. Gửi
/// trước khi lưu là báo cho người dùng về một giao dịch có thể bị rollback ngay sau đó.
/// </summary>
public interface ITransactionConfirmationEffects
{
    /// <summary>
    /// Set <c>transaction.Status = Confirmed</c>, cộng/trừ <c>account.BalanceCents</c>, áp budget
    /// delta. KHÔNG gọi SaveChangesAsync, KHÔNG gửi gì — chỉ mutate trong bộ nhớ.
    /// </summary>
    Task<IReadOnlyList<BudgetAlert>> ApplyAsync(
        Transaction transaction, FinancialAccount account, CancellationToken ct = default);

    /// <summary>
    /// Gọi SAU khi transaction/account đã được lưu: xoá cache tổng hợp, gửi cảnh báo ngân sách
    /// (<paramref name="budgetAlerts"/> lấy từ <see cref="ApplyAsync"/>) và thông báo biến động
    /// số dư trong app.
    /// </summary>
    Task NotifyAsync(
        Transaction transaction, IReadOnlyList<BudgetAlert> budgetAlerts, CancellationToken ct = default);
}
