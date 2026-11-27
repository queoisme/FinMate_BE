using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Forecasting;
using FinMate.Backtest.Generation;
using FinMate.Domain.Entities;

namespace FinMate.Backtest.Simulation;

/// <summary>
/// Dựng <see cref="ForecastService"/> THẬT trên một thế giới tổng hợp: repository giả đọc qua <see cref="WorldView"/>, đồng hồ giả qua ctor
/// <c>internal</c> sẵn có. Phần ghép nối của production (chọn ví, lọc khoản định kỳ, loại trừ, chuyển khoản vượt ranh giới, vay) chạy nguyên vẹn.
/// </summary>
public sealed class ForecastHarness
{
    private readonly ForecastService _service;
    private readonly WorldView _truth;
    private readonly HashSet<Guid> _counted;

    public ForecastHarness(SynthWorld world, ForecastSettings settings, int configVersion = 0)
        : this(world, settings, configVersion, view => () => OracleRecurring.ItemsAt(view.World, view.Clock.Now))
    {
    }

    public ForecastHarness(SynthWorld world, ForecastSettings settings, int configVersion, Func<WorldView, Func<IReadOnlyList<RecurringItem>>> items)
    {
        Clock = new BacktestClock();
        View = new WorldView(world, Clock);
        _truth = new WorldView(world, new BacktestClock { Now = GeneratorSpec.At(GeneratorSpec.DataEnd.AddDays(1), 0) });
        _counted = world.Accounts.Where(a => a.IncludeInForecast).Select(a => a.Id).ToHashSet();
        Settings = settings;
        ConfigVersion = configVersion;
        var config = new FakeConfig(new SystemConfigSnapshot(configVersion, new SystemConfigDocument { Forecast = settings }, null));
        _service = new ForecastService(
            new FakeAccounts(View), new FakeRecurringItems(items(View)), new FakeForecastData(View), new FakeBudgets(View), new UnusedUsers(), new UnusedGoals(),
            config, new NoCache(), () => Clock.Now, new FakeLoans(View));
    }

    public BacktestClock Clock { get; }

    public WorldView View { get; }

    public ForecastSettings Settings { get; }

    public int ConfigVersion { get; }

    /// <summary>Dự báo dòng tiền tổng (mọi ví được tính) tại mốc <paramref name="origin"/>, tới <paramref name="origin"/> + <paramref name="horizonDays"/>.</summary>
    public CashflowForecastDto ForecastAt(DateOnly origin, int horizonDays)
    {
        Clock.SetOrigin(origin);
        return _service.GetCashflowAsync(View.UserId, null, origin.AddDays(horizonDays)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Số dư thực tế cuối ngày <paramref name="to"/> của tập ví được tính = số dư lúc chụp + thu chi thô từ <paramref name="origin"/> tới <paramref name="to"/>
    /// (gồm vay, hoàn tiền) + chuyển khoản vượt ranh giới. Đúng định nghĩa của <c>ForecastAccuracyService.FillActualsAsync</c> (D9).
    /// </summary>
    public long ActualBalance(long startBalance, DateOnly origin, DateOnly to)
    {
        var rows = _truth.Raw(origin, to).Concat(_truth.Boundary(_counted, origin, to));
        return startBalance + ForecastActuals.NetFlow(rows, _counted, origin, to);
    }

    /// <summary>Tổng số dư các ví được tính tại thời điểm của đồng hồ.</summary>
    public long CountedBalanceNow() => _counted.Sum(View.BalanceOf);
}
