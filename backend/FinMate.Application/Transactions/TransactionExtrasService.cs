using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>Văn bản biên lai để app mở khung chia sẻ của hệ điều hành. Không có đường link công khai: giao dịch là dữ liệu riêng tư.</summary>
public sealed record ShareReceiptDto(string Title, string Text);

public interface ITransactionExtrasService
{
    Task<TransactionDto> AttachGoalAsync(Guid userId, Guid transactionId, Guid goalId, CancellationToken ct = default);
    Task<TransactionDto> DetachGoalAsync(Guid userId, Guid transactionId, CancellationToken ct = default);
    Task<ShareReceiptDto> ShareReceiptAsync(Guid userId, Guid transactionId, CancellationToken ct = default);
    Task<RecurringItemDto> CreateRecurringAsync(Guid userId, Guid transactionId, RecurringCadence? cadence, CancellationToken ct = default);
}

/// <summary>Các thao tác nhỏ trên một giao dịch đã có (gắn mục tiêu, chia sẻ biên lai, tạo khoản định kỳ). Mọi truy vấn đi qua userId.</summary>
public class TransactionExtrasService : ITransactionExtrasService
{
    private readonly ITransactionRepository _transactions;
    private readonly ISavingGoalRepository _goals;
    private readonly IRecurringItemService _recurring;

    public TransactionExtrasService(ITransactionRepository transactions, ISavingGoalRepository goals, IRecurringItemService recurring)
    {
        _transactions = transactions;
        _goals = goals;
        _recurring = recurring;
    }

    public async Task<TransactionDto> AttachGoalAsync(Guid userId, Guid transactionId, Guid goalId, CancellationToken ct = default)
    {
        var t = await GetAsync(userId, transactionId, ct);
        if (t.TransactionType == TransactionType.Transfer)
        {
            throw new BusinessRuleException(TransactionErrorCodes.GoalLinkNotAllowed, "Giao dịch chuyển ví không gắn được vào mục tiêu tiết kiệm.");
        }

        var goal = await _goals.GetByIdAsync(goalId, userId, ct) ?? throw new NotFoundException("SavingGoal", goalId);
        t.SavingGoalId = goal.Id;
        t.SavingGoal = goal;
        return await SaveAsync(t, ct);
    }

    public async Task<TransactionDto> DetachGoalAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var t = await GetAsync(userId, transactionId, ct);
        t.SavingGoalId = null;
        t.SavingGoal = null;
        return await SaveAsync(t, ct);
    }

    public async Task<ShareReceiptDto> ShareReceiptAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var t = await GetAsync(userId, transactionId, ct);
        var what = t.MerchantName ?? t.Description ?? "Giao dịch";
        var when = VietnamTime.ToLocal(t.TransactedAt).ToString("dd/MM/yyyy HH:mm");
        var kind = t.TransactionType switch { TransactionType.Credit => "Thu", TransactionType.Transfer => "Chuyển ví", _ => "Chi" };
        var lines = new List<string>
        {
            $"{kind}: {what}",
            // Cùng cách định dạng với ChatCommandHandler: không phụ thuộc culture của máy chủ.
            $"Số tiền: {t.AmountCents.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.')} ₫",
            $"Thời gian: {when}",
        };
        if (t.Category is not null) lines.Add($"Danh mục: {t.Category.Name}");
        if (t.FinancialAccount is not null) lines.Add($"Ví: {t.FinancialAccount.AccountName}");
        if (!string.IsNullOrWhiteSpace(t.Description) && t.Description != what) lines.Add($"Ghi chú: {t.Description}");
        return new ShareReceiptDto($"Biên lai {what}", string.Join("\n", lines));
    }

    public async Task<RecurringItemDto> CreateRecurringAsync(Guid userId, Guid transactionId, RecurringCadence? cadence, CancellationToken ct = default)
    {
        var t = await GetAsync(userId, transactionId, ct);
        var name = (t.MerchantName ?? t.Description)?.Trim();
        if (t.Status != TransactionStatus.Confirmed || t.TransactionType == TransactionType.Transfer || string.IsNullOrEmpty(name))
        {
            throw new BusinessRuleException(TransactionErrorCodes.RecurringNotAllowed,
                "Chỉ giao dịch thu/chi đã xác nhận và có tên nơi nhận hoặc mô tả mới tạo được khoản định kỳ.");
        }

        var step = cadence ?? RecurringCadence.Monthly;
        var due = VietnamTime.DateOf(t.TransactedAt);
        var today = VietnamTime.Today();
        do
        {
            due = step switch { RecurringCadence.Weekly => due.AddDays(7), RecurringCadence.Yearly => due.AddYears(1), _ => due.AddMonths(1) };
        }
        while (due <= today);

        return await _recurring.CreateAsync(
            new CreateRecurringItemCommand(
                userId,
                t.TransactionType == TransactionType.Credit ? RecurringDirection.Inflow : RecurringDirection.Outflow,
                name, t.MerchantName, t.AmountCents, step, due, t.CategoryId, t.FinancialAccountId),
            ct);
    }

    private async Task<Domain.Entities.Transaction> GetAsync(Guid userId, Guid id, CancellationToken ct)
        => await _transactions.GetByIdAsync(id, userId, ct) ?? throw new NotFoundException("Transaction", id);

    private async Task<TransactionDto> SaveAsync(Domain.Entities.Transaction t, CancellationToken ct)
    {
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _transactions.UpdateAsync(t, ct);
        return TransactionMapper.ToDto(t);
    }
}
