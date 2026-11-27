using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Backtest.Simulation;

/// <summary>
/// Chế độ "đáp án" (D8, đặc tả §8): khoản định kỳ thật như người dùng khai đầy đủ, nhưng chỉ dùng thông tin trước <c>now</c>. Đây là cận trên:
/// mô hình được biết khoản nào là định kỳ. Khoản dao động và chu kỳ production không hỗ trợ không có ở đây.
/// </summary>
public sealed class OracleRecurring
{
    private readonly SynthWorld _world;
    private readonly Dictionary<Guid, DateTimeOffset> _paidAt;

    public OracleRecurring(SynthWorld world)
    {
        _world = world;
        _paidAt = world.Transactions.ToDictionary(t => t.Id, t => t.At);
    }

    public static IReadOnlyList<RecurringItem> ItemsAt(SynthWorld world, DateTimeOffset now) => new OracleRecurring(world).ItemsAt(now);

    public IReadOnlyList<RecurringItem> ItemsAt(DateTimeOffset now)
    {
        var world = _world;
        var paidAt = _paidAt;
        var items = new List<RecurringItem>();
        foreach (var truth in world.Recurring)
        {
            if (truth.Volatile || truth.Cadence is not (RecurringCadence.Monthly or RecurringCadence.Weekly))
            {
                continue;
            }

            if (ItemAt(world, truth, now, paidAt) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static RecurringItem? ItemAt(SynthWorld world, TruthRecurring truth, DateTimeOffset now, IReadOnlyDictionary<Guid, DateTimeOffset> paidAt)
    {
        var occurrences = truth.Occurrences;
        var lastPaid = -1;
        for (var i = 0; i < occurrences.Count; i++)
        {
            var o = occurrences[i];
            if (o.TransactionId is { } id && paidAt.TryGetValue(id, out var at) && at < now && o.PaidOn >= world.FirstDay)
            {
                lastPaid = i;
            }
        }

        var next = lastPaid >= 0 ? lastPaid + 1 : FirstIndexFrom(occurrences, world.FirstDay);
        if (next < 0 || next >= occurrences.Count)
        {
            return null;
        }

        var amount = lastPaid >= 0 ? occurrences[lastPaid].PaidCents!.Value : occurrences[next].NominalCents;
        var created = GeneratorSpec.At(world.FirstDay, 0);
        return new RecurringItem
        {
            Id = DeterministicGuid.V5(GeneratorSpec.IdNamespace, $"{world.UserId}|oracle:{truth.Name}"),
            UserId = world.UserId,
            Direction = truth.Direction,
            Name = truth.Name,
            CounterpartyKey = CounterpartyKey.From(truth.Name),
            CategoryId = GeneratorSpec.CategoryId(truth.CategorySlug),
            FinancialAccountId = truth.AccountId,
            AmountCents = amount,
            Cadence = truth.Cadence,
            NextDueDate = occurrences[next].Due,
            AnchorDay = truth.AnchorDay,
            Status = RecurringStatus.Active,
            Source = RecurringSource.Manual,
            CreatedAt = created,
            UpdatedAt = created,
        };
    }

    private static int FirstIndexFrom(IReadOnlyList<TruthOccurrence> occurrences, DateOnly from)
    {
        for (var i = 0; i < occurrences.Count; i++)
        {
            if (occurrences[i].Due >= from)
            {
                return i;
            }
        }

        return -1;
    }
}
