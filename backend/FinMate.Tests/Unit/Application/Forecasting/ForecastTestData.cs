using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;

namespace FinMate.Tests.Unit.Application.Forecasting;

internal static class ForecastTestData
{
    public static readonly Guid Account = Guid.NewGuid();

    /// <summary>Thứ Hai 2026-11-02 — cố định để test không phụ thuộc ngày chạy.</summary>
    public static readonly DateOnly Today = new(2026, 11, 2);

    public static ForecastRules Rules => ForecastRules.From(new ForecastSettings());

    public static ForecastTransaction Debit(DateOnly date, long amount, string? merchant = null, bool excluded = false, Guid? account = null)
        => Tx(date, amount, TransactionType.Debit, merchant, excluded, account);

    public static ForecastTransaction Credit(DateOnly date, long amount, string? merchant = null, Guid? account = null)
        => Tx(date, amount, TransactionType.Credit, merchant, false, account);

    public static RecurringItemView Item(
        string name, long amount, DateOnly nextDue,
        RecurringDirection direction = RecurringDirection.Outflow,
        RecurringCadence cadence = RecurringCadence.Monthly,
        Guid? account = null)
        => new(Guid.NewGuid(), direction, name, CounterpartyKey.From(name), account, amount, cadence, nextDue,
            cadence == RecurringCadence.Weekly ? null : nextDue.Day);

    private static ForecastTransaction Tx(DateOnly date, long amount, TransactionType type, string? merchant, bool excluded, Guid? account)
    {
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), VietnamTime.Offset);
        return new ForecastTransaction(
            Guid.NewGuid(), account ?? Account, null, amount, type, at, date, merchant, CounterpartyKey.From(merchant), excluded);
    }
}
