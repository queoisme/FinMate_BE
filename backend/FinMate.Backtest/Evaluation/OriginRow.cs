namespace FinMate.Backtest.Evaluation;

public static class Methods
{
    public const string Model = "model";
    public const string Hold = "B-hold";
    public const string RecurringOnly = "B0";
    public const string Mean30 = "B-mean30";
    public const string Naive = "B-naive";

    public static readonly IReadOnlyList<string> Balance = [Model, Hold, RecurringOnly, Mean30, Naive];
    public static readonly IReadOnlyList<string> Flexible = [Model, Mean30, Naive];
}

public static class Modes
{
    /// <summary>Chế độ đáp án (D8): cận trên.</summary>
    public const string Oracle = "oracle";
}

/// <summary>
/// Kết quả của một (người, kịch bản, mốc, tầm): số dư thực tế và dự báo của mô hình + từng baseline, chi linh hoạt dự báo và thực tế.
/// <see cref="User"/> là người gốc (hồ sơ × hạt giống), dùng để bootstrap theo người (D10).
/// </summary>
public sealed record OriginRow(
    string Mode,
    string Scenario,
    string Persona,
    int User,
    DateOnly Origin,
    int Horizon,
    bool LowConfidence,
    bool Affected,
    long Actual,
    long Start,
    long Expected,
    long Low,
    long High,
    long RecurringOnly,
    long Mean30,
    long Naive,
    long FlexActual,
    long FlexModel,
    long FlexMean30,
    long FlexNaive)
{
    public long Predicted(string method) => method switch
    {
        Methods.Model => Expected,
        Methods.Hold => Start,
        Methods.RecurringOnly => RecurringOnly,
        Methods.Mean30 => Mean30,
        Methods.Naive => Naive,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public long PredictedFlexible(string method) => method switch
    {
        Methods.Model => FlexModel,
        Methods.Mean30 => FlexMean30,
        Methods.Naive => FlexNaive,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public bool InsideBand => Actual >= Math.Min(Low, High) && Actual <= Math.Max(Low, High);

    public long BandWidth => Math.Abs(High - Low);
}
