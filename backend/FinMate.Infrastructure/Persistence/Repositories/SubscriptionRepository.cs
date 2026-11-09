using FinMate.Application.Common.Interfaces;
using FinMate.Application.Subscriptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly FinMateDbContext _context;

    public SubscriptionRepository(FinMateDbContext context)
    {
        _context = context;
    }

    public Task<List<SubscriptionPlan>> ListPlansAsync(bool activeOnly, CancellationToken ct = default)
        => _context.SubscriptionPlans.AsNoTracking().Include(p => p.Features)
            .Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Code)
            .ToListAsync(ct);

    public Task<SubscriptionPlan?> GetPlanForUpdateAsync(Guid id, CancellationToken ct = default)
        => _context.SubscriptionPlans.Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<SubscriptionPlan?> GetPlanAsync(Guid id, CancellationToken ct = default)
        => _context.SubscriptionPlans.AsNoTracking().Include(p => p.Features).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<SubscriptionPlan?> GetDefaultFreePlanAsync(CancellationToken ct = default)
        => _context.SubscriptionPlans.AsNoTracking().Include(p => p.Features).FirstOrDefaultAsync(p => p.IsDefaultFree, ct);

    public Task<bool> PlanCodeExistsAsync(string code, CancellationToken ct = default)
        => _context.SubscriptionPlans.AnyAsync(p => p.Code == code, ct);

    public void AddPlan(SubscriptionPlan plan) => _context.SubscriptionPlans.Add(plan);

    public void RemoveFeatures(IEnumerable<SubscriptionPlanFeature> features) => _context.SubscriptionPlanFeatures.RemoveRange(features);

    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public void AddOrder(SubscriptionOrder order) => _context.SubscriptionOrders.Add(order);

    public Task<SubscriptionOrder?> GetOrderByInvoiceAsync(string invoiceNumber, CancellationToken ct = default)
        => _context.SubscriptionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.InvoiceNumber == invoiceNumber, ct);

    public Task<SubscriptionOrder?> GetUserOrderByInvoiceAsync(Guid userId, string invoiceNumber, CancellationToken ct = default)
        => _context.SubscriptionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.UserId == userId && o.InvoiceNumber == invoiceNumber, ct);

    public Task<List<SubscriptionOrder>> ListUserOrdersAsync(Guid userId, int limit, CancellationToken ct = default)
        => _context.SubscriptionOrders.AsNoTracking().Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);

    public async Task<(List<SubscriptionOrder> Items, int Total)> ListOrdersAsync(SubscriptionOrderStatus? status, int offset, int limit, CancellationToken ct = default)
    {
        var query = _context.SubscriptionOrders.AsNoTracking().Where(o => status == null || o.Status == status);
        return (await query.OrderByDescending(o => o.CreatedAt).Skip(offset).Take(limit).ToListAsync(ct), await query.CountAsync(ct));
    }

    public Task<int> CountPendingOrdersAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => _context.SubscriptionOrders.CountAsync(o => o.UserId == userId && o.Status == SubscriptionOrderStatus.Pending && o.ExpiresAt > now, ct);

    public Task<List<SubscriptionOrder>> ListOrdersDueForCheckAsync(DateTimeOffset now, DateTimeOffset lateSince, int limit, CancellationToken ct = default)
        => _context.SubscriptionOrders.AsNoTracking()
            .Where(o => o.NextCheckAt <= now
                && (o.Status == SubscriptionOrderStatus.Pending
                    || ((o.Status == SubscriptionOrderStatus.Expired || o.Status == SubscriptionOrderStatus.Cancelled) && o.CreatedAt >= lateSince)))
            .OrderBy(o => o.NextCheckAt)
            .Take(limit).ToListAsync(ct);

    public Task SetNextCheckAsync(Guid orderId, DateTimeOffset nextCheckAt, CancellationToken ct = default)
        => _context.SubscriptionOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.NextCheckAt, nextCheckAt), ct);

    public Task MarkPendingAsync(Guid orderId, SubscriptionOrderStatus status, CancellationToken ct = default)
        => _context.SubscriptionOrders.Where(o => o.Id == orderId && o.Status == SubscriptionOrderStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status), ct);

    public async Task<bool> MarkNeedsReviewAsync(Guid orderId, string reason, CancellationToken ct = default)
        => await _context.SubscriptionOrders
            .Where(o => o.Id == orderId && o.Status != SubscriptionOrderStatus.Paid && o.Status != SubscriptionOrderStatus.Voided && o.Status != SubscriptionOrderStatus.NeedsReview)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, SubscriptionOrderStatus.NeedsReview).SetProperty(o => o.ReviewReason, reason), ct) > 0;

    public async Task<PaymentOutcome> RecordPaymentAsync(Guid orderId, string? sepayTransactionId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var order = await LockOrderAsync(orderId, ct);
        if (order is null)
        {
            return PaymentOutcome.OrderMissing;
        }

        if (order.Status is SubscriptionOrderStatus.Paid or SubscriptionOrderStatus.Voided or SubscriptionOrderStatus.NeedsReview)
        {
            return PaymentOutcome.AlreadySettled;
        }

        if (order.UserId is not { } userId)
        {
            order.Status = SubscriptionOrderStatus.NeedsReview;
            order.ReviewReason = SubscriptionSettlementService.ReasonUserMissing;
            order.SepayTransactionId = sepayTransactionId;
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return PaymentOutcome.UserMissing;
        }

        await LockUserPeriodsAsync(userId, ct);
        var lastEnd = await _context.UserSubscriptionPeriods
            .Where(p => p.UserId == userId && p.VoidedAt == null)
            .MaxAsync(p => (DateTimeOffset?)p.EndsAt, ct);
        var start = SubscriptionPeriods.NextStart(now, lastEnd);

        order.Status = SubscriptionOrderStatus.Paid;
        order.PaidAt = now;
        order.SepayTransactionId = sepayTransactionId;
        _context.UserSubscriptionPeriods.Add(new UserSubscriptionPeriod
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = order.Id,
            PlanCode = order.PlanCode,
            PlanName = order.PlanName,
            FeaturesJson = order.FeaturesJson,
            StartsAt = start,
            EndsAt = SubscriptionPeriods.EndOf(start, order.Cycle),
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return PaymentOutcome.Granted;
    }

    public async Task<bool> VoidPaymentAsync(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        var order = await LockOrderAsync(orderId, ct);
        if (order is null || order.Status != SubscriptionOrderStatus.Paid)
        {
            return false;
        }

        order.Status = SubscriptionOrderStatus.Voided;
        order.VoidedAt = now;
        if (order.UserId is { } userId)
        {
            await LockUserPeriodsAsync(userId, ct);
            var periods = await _context.UserSubscriptionPeriods.Where(p => p.UserId == userId && p.VoidedAt == null).ToListAsync(ct);
            var voided = periods.FirstOrDefault(p => p.OrderId == order.Id);
            if (voided is not null)
            {
                voided.VoidedAt = now;
                var shift = SubscriptionPeriods.UnusedOf(voided.StartsAt, voided.EndsAt, now);
                foreach (var later in periods.Where(p => p.StartsAt >= voided.EndsAt))
                {
                    later.StartsAt -= shift;
                    later.EndsAt -= shift;
                }
            }
        }

        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<List<UserSubscriptionPeriod>> ListCurrentAndUpcomingPeriodsAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
        => _context.UserSubscriptionPeriods.AsNoTracking()
            .Where(p => p.UserId == userId && p.VoidedAt == null && p.EndsAt > now)
            .OrderBy(p => p.StartsAt).ToListAsync(ct);

    public Task<List<UserSubscriptionPeriod>> ListExpiringWithoutRenewalAsync(DateTimeOffset now, DateTimeOffset firstReminderUntil, DateTimeOffset lastReminderUntil, int limit, CancellationToken ct = default)
        => _context.UserSubscriptionPeriods
            .Where(p => p.VoidedAt == null && p.EndsAt > now && p.EndsAt <= firstReminderUntil)
            .Where(p => p.Reminded7dAt == null || (p.Reminded1dAt == null && p.EndsAt <= lastReminderUntil))
            .Where(p => !_context.UserSubscriptionPeriods.Any(next => next.UserId == p.UserId && next.VoidedAt == null && next.StartsAt >= p.EndsAt))
            .OrderBy(p => p.EndsAt).Take(limit).ToListAsync(ct);

    public Task<List<SubscriptionOrder>> ListUnalertedReviewOrdersAsync(int limit, CancellationToken ct = default)
        => _context.SubscriptionOrders.AsNoTracking()
            .Where(o => o.Status == SubscriptionOrderStatus.NeedsReview && o.ReviewAlertedAt == null)
            .OrderBy(o => o.CreatedAt).Take(limit).ToListAsync(ct);

    public Task MarkReviewAlertedAsync(Guid orderId, DateTimeOffset now, CancellationToken ct = default)
        => _context.SubscriptionOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewAlertedAt, now), ct);

    public async Task<bool> TryConsumeAsync(Guid userId, string featureKey, DateOnly month, int limit, CancellationToken ct = default)
        => await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO subscription_usage (user_id, feature_key, month, used)
            VALUES ({userId}, {featureKey}, {month}, 1)
            ON CONFLICT (user_id, feature_key, month)
            DO UPDATE SET used = subscription_usage.used + 1
            WHERE subscription_usage.used < {limit}
            """, ct) > 0;

    public Task ReleaseAsync(Guid userId, string featureKey, DateOnly month, CancellationToken ct = default)
        => _context.SubscriptionUsages.Where(u => u.UserId == userId && u.FeatureKey == featureKey && u.Month == month && u.Used > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Used, u => u.Used - 1), ct);

    public Task<List<SubscriptionUsage>> ListUsageAsync(Guid userId, DateOnly month, CancellationToken ct = default)
        => _context.SubscriptionUsages.AsNoTracking().Where(u => u.UserId == userId && u.Month == month).ToListAsync(ct);

    private Task<SubscriptionOrder?> LockOrderAsync(Guid orderId, CancellationToken ct)
        => _context.SubscriptionOrders.FromSqlInterpolated($"SELECT * FROM subscription_orders WHERE id = {orderId} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <summary>Hai đơn của cùng một người về cùng lúc phải xếp hàng, nếu không cả hai đọc cùng kỳ cuối và hai kỳ chồng lên nhau.</summary>
    private Task LockUserPeriodsAsync(Guid userId, CancellationToken ct)
        => _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 0))", ct);
}
