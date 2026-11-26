using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public sealed record RecurringPaymentDto(Guid Id, Guid? TransactionId, DateOnly DueDate, string Kind, DateTimeOffset CreatedAt);

public sealed record RecurringPaymentResultDto(RecurringItemDto Item, RecurringPaymentDto Payment);

/// <summary>Kỳ đã trả của khoản định kỳ: xem, đánh dấu tay, gỡ (BR-INS-04), và tất toán trước hạn (BR-INS-08). Phase 92.</summary>
public interface IRecurringInstallmentService
{
    Task<IReadOnlyList<RecurringPaymentDto>> ListPaymentsAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>Ghi kỳ đang đến hạn là đã trả, gắn giao dịch có sẵn hoặc không (tiền mặt), rồi đẩy ngày đến hạn sang kỳ sau.</summary>
    Task<RecurringPaymentResultDto> AddPaymentAsync(Guid userId, Guid itemId, Guid? transactionId, CancellationToken ct = default);

    /// <summary>Gỡ một kỳ: lùi ngày đến hạn một kỳ (D4) và mở lại khoản nếu thiếu kỳ. Gọi lại với kỳ đã gỡ vẫn thành công.</summary>
    Task RemovePaymentAsync(Guid userId, Guid itemId, Guid paymentId, CancellationToken ct = default);

    /// <summary>Tất toán: hoàn thành ngay, <c>settledEarly</c>. Khoản đã hoàn thành → trả như cũ.</summary>
    Task<RecurringItemDto> SettleAsync(Guid userId, Guid itemId, Guid? transactionId, CancellationToken ct = default);
}

public class RecurringInstallmentService : IRecurringInstallmentService
{
    private readonly IRecurringItemRepository _items;
    private readonly ITransactionRepository _transactions;
    private readonly ICacheService _cache;

    public RecurringInstallmentService(IRecurringItemRepository items, ITransactionRepository transactions, ICacheService cache)
    {
        _items = items;
        _transactions = transactions;
        _cache = cache;
    }

    public async Task<IReadOnlyList<RecurringPaymentDto>> ListPaymentsAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        await GetOwnedAsync(userId, itemId, ct);
        return (await _items.ListPaymentsAsync(userId, itemId, ct)).Select(ToDto).ToList();
    }

    public async Task<RecurringPaymentResultDto> AddPaymentAsync(Guid userId, Guid itemId, Guid? transactionId, CancellationToken ct = default)
    {
        var result = await _items.RunWithUserLockedAsync(userId, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var item = await GetOwnedAsync(userId, itemId, ct);
            if (item.Status is not (RecurringStatus.Active or RecurringStatus.Paused))
            {
                throw StateConflict(item, "Chỉ đánh dấu đã trả được khoản đang theo dõi hoặc tạm dừng.");
            }

            await EnsureAttachableAsync(userId, item, transactionId, ct);
            var payment = NewPayment(item, transactionId, RecurringPaymentKind.Manual, now);
            item.NextDueDate = RecurringSchedule.Next(item.NextDueDate, item.Cadence, RecurringSchedule.DayOf(item));
            item.UpdatedAt = now;
            RecurringCompletion.Apply(item, VietnamTime.DateOf(now), now);
            await _items.SaveChangesAsync(ct);
            return new RecurringPaymentResultDto(item.ToDto(VietnamTime.DateOf(now)), ToDto(payment));
        }, ct);

        await InvalidateAsync(userId, ct);
        return result;
    }

    public async Task RemovePaymentAsync(Guid userId, Guid itemId, Guid paymentId, CancellationToken ct = default)
    {
        var removed = await _items.RunWithUserLockedAsync(userId, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var item = await GetOwnedAsync(userId, itemId, ct);
            var payment = await _items.GetPaymentAsync(userId, itemId, paymentId, ct) ?? throw new NotFoundException("RecurringItemPayment", paymentId);
            if (payment.DeletedAt is not null)
            {
                return false;
            }

            RecurringPaymentLinks.Release(item, payment, now);
            await _items.SaveChangesAsync(ct);
            return true;
        }, ct);

        if (removed)
        {
            await InvalidateAsync(userId, ct);
        }
    }

    public async Task<RecurringItemDto> SettleAsync(Guid userId, Guid itemId, Guid? transactionId, CancellationToken ct = default)
    {
        var (dto, changed) = await _items.RunWithUserLockedAsync(userId, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var today = VietnamTime.DateOf(now);
            var item = await GetOwnedAsync(userId, itemId, ct);
            if (!RecurringCompletion.HasEnd(item))
            {
                throw new BusinessRuleException(ForecastErrorCodes.RecurringNoFinal, "Chỉ tất toán được khoản có số kỳ hoặc ngày kết thúc.");
            }

            if (item.Status == RecurringStatus.Completed)
            {
                return (item.ToDto(today), false);
            }

            if (item.Status is not (RecurringStatus.Active or RecurringStatus.Paused))
            {
                throw StateConflict(item, "Chỉ tất toán được khoản đang theo dõi hoặc tạm dừng.");
            }

            if (transactionId is not null)
            {
                await EnsureAttachableAsync(userId, item, transactionId, ct);
                NewPayment(item, transactionId, RecurringPaymentKind.Settlement, now);
            }

            item.Status = RecurringStatus.Completed;
            item.CompletedAt = RecurringCompletion.ToMicroseconds(now);
            item.SettledEarly = true;
            item.UpdatedAt = now;
            await _items.SaveChangesAsync(ct);
            return (item.ToDto(today), true);
        }, ct);

        if (changed)
        {
            await InvalidateAsync(userId, ct);
        }

        return dto;
    }

    /// <summary>BR-INS-04: giao dịch của chính user, đã xác nhận, đúng chiều tiền, chưa gắn khoản định kỳ nào.</summary>
    private async Task EnsureAttachableAsync(Guid userId, RecurringItem item, Guid? transactionId, CancellationToken ct)
    {
        if (transactionId is not { } id)
        {
            return;
        }

        var transaction = await _transactions.GetByIdAsync(id, userId, ct) ?? throw new NotFoundException("Transaction", id);
        var expected = item.Direction == RecurringDirection.Outflow ? TransactionType.Debit : TransactionType.Credit;
        if (transaction.Status != TransactionStatus.Confirmed || transaction.TransactionType != expected)
        {
            throw new BusinessRuleException(
                ForecastErrorCodes.RecurringPaymentInvalid, "Chỉ gắn được giao dịch đã xác nhận, cùng chiều tiền với khoản định kỳ.");
        }

        if (await _items.FindLivePaymentByTransactionAsync(userId, id, ct) is not null)
        {
            throw new ConflictException(ForecastErrorCodes.RecurringPaymentLinked, "Giao dịch này đã được gắn với một kỳ của khoản định kỳ.");
        }
    }

    private RecurringItemPayment NewPayment(RecurringItem item, Guid? transactionId, RecurringPaymentKind kind, DateTimeOffset now)
    {
        var payment = new RecurringItemPayment
        {
            Id = Guid.NewGuid(),
            UserId = item.UserId,
            RecurringItemId = item.Id,
            TransactionId = transactionId,
            DueDate = item.NextDueDate,
            Kind = kind,
            CreatedAt = now,
        };
        _items.AddPayment(payment);
        item.LivePaymentCount++;
        return payment;
    }

    private static ConflictException StateConflict(RecurringItem item, string message)
        => new(ForecastErrorCodes.RecurringState, $"{message} Khoản đang ở trạng thái '{RecurringItemMapping.Text(item.Status)}'.");

    private async Task<RecurringItem> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct)
        => await _items.GetAsync(id, userId, ct) ?? throw new NotFoundException("RecurringItem", id);

    private Task InvalidateAsync(Guid userId, CancellationToken ct)
        => _cache.RemoveAsync(CacheKeys.ForecastBaseline(userId, VietnamTime.Today()), ct);

    private static RecurringPaymentDto ToDto(RecurringItemPayment p)
        => new(p.Id, p.TransactionId, p.DueDate, RecurringItemMapping.Text(p.Kind), p.CreatedAt);
}

/// <summary>
/// Gỡ một liên kết thanh toán và đưa khoản về đúng trạng thái (Phase 92, BR-INS-03/06, D4): dùng chung cho gỡ tay và cho các đường sửa giao dịch
/// (xoá, đảo, chuyển thành Transfer…). Không lưu: người gọi lưu trong cùng lượt.
/// </summary>
public static class RecurringPaymentLinks
{
    public static void Release(RecurringItem item, RecurringItemPayment payment, DateTimeOffset now)
    {
        payment.DeletedAt = now;
        item.LivePaymentCount = Math.Max(0, item.LivePaymentCount - 1);
        if (payment.Kind != RecurringPaymentKind.Settlement)
        {
            // Mỗi kỳ trả đã đẩy ngày đến hạn một kỳ; gỡ thì lùi đúng một kỳ để "ngày đến hạn = kỳ đã trả + 1" (D4, giống Phase 81 R1).
            item.NextDueDate = RecurringSchedule.Previous(item.NextDueDate, item.Cadence, RecurringSchedule.DayOf(item));
        }

        item.UpdatedAt = now;
        RecurringCompletion.Apply(item, VietnamTime.DateOf(now), now);
    }
}
