using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Giữ liên kết kỳ đã trả của khoản định kỳ khớp với các đường sửa giao dịch (Phase 92, BR-INS-03): xoá, đảo, chuyển thành Transfer hoặc chi quỹ
/// nhóm → mất kỳ (D12); đổi chiều tiền → mất kỳ (D11); gộp → liên kết sang bản giữ lại. Khoản được tính lại ngay (mở lại nếu thiếu kỳ).
/// Mọi hàm chỉ đổi dữ liệu đang được theo dõi, KHÔNG lưu: người gọi lưu trong cùng lượt với thay đổi của giao dịch.
/// </summary>
public interface IRecurringPaymentSync
{
    /// <summary>Giao dịch sắp không còn là một lần trả (xoá mềm, đảo, chuyển thành Transfer/chi quỹ).</summary>
    Task OnTransactionRemovedAsync(Guid userId, Guid transactionId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>D11: chỉ đổi chiều tiền mới gỡ; sửa số tiền, ngày, người nhận, danh mục giữ liên kết.</summary>
    Task OnTypeChangingAsync(Guid userId, Transaction transaction, TransactionType? newType, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Gộp: liên kết của bản bị gộp chuyển sang bản giữ lại; bản giữ lại đã có liên kết (hoặc là Transfer) thì liên kết của bản bị gộp mất.</summary>
    Task OnMergedAsync(Guid userId, Transaction loser, Transaction survivor, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class RecurringPaymentSync : IRecurringPaymentSync
{
    private readonly IRecurringItemRepository _items;

    public RecurringPaymentSync(IRecurringItemRepository items)
    {
        _items = items;
    }

    public async Task OnTransactionRemovedAsync(Guid userId, Guid transactionId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await _items.FindLivePaymentByTransactionAsync(userId, transactionId, ct) is { } payment)
        {
            await ReleaseAsync(userId, payment, now, ct);
        }
    }

    public Task OnTypeChangingAsync(Guid userId, Transaction transaction, TransactionType? newType, DateTimeOffset now, CancellationToken ct = default)
        => newType is { } type && type != transaction.TransactionType
            ? OnTransactionRemovedAsync(userId, transaction.Id, now, ct)
            : Task.CompletedTask;

    public async Task OnMergedAsync(Guid userId, Transaction loser, Transaction survivor, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await _items.FindLivePaymentByTransactionAsync(userId, loser.Id, ct) is not { } payment)
        {
            return;
        }

        if (survivor.TransactionType == TransactionType.Transfer || await _items.FindLivePaymentByTransactionAsync(userId, survivor.Id, ct) is not null)
        {
            await ReleaseAsync(userId, payment, now, ct);
            return;
        }

        payment.TransactionId = survivor.Id;   // cùng một kỳ, cùng khoản: số kỳ đã trả không đổi
    }

    private async Task ReleaseAsync(Guid userId, RecurringItemPayment payment, DateTimeOffset now, CancellationToken ct)
    {
        var item = await _items.GetAsync(payment.RecurringItemId, userId, ct);
        if (item is null)
        {
            payment.DeletedAt = now;
            return;
        }

        RecurringPaymentLinks.Release(item, payment, now);
    }
}
