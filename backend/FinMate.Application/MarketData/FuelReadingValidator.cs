using FinMate.Application.Admin.SystemConfig;

namespace FinMate.Application.MarketData;

public static class FuelErrorCodes
{
    public const string MissingFuels = "MISSING_FUELS";
    public const string PriceOutOfRange = "PRICE_OUT_OF_RANGE";
    public const string RegionMismatch = "REGION_MISMATCH";
    public const string ChangeTooLarge = "CHANGE_TOO_LARGE";
}

/// <summary>Cửa duy nhất để một con số được coi là giá xăng, dù đọc từ trang web hay do admin sửa tay.</summary>
public static class FuelReadingValidator
{
    /// <summary>Kiểm tra cả bảng đọc từ một nguồn. Trả mã lỗi đầu tiên, null nếu đạt.</summary>
    public static string? ValidateTable(IReadOnlyList<FuelReading> readings, MarketDataSettings settings)
    {
        if (FuelKeys.Required.Any(key => readings.All(r => r.FuelKey != key)))
        {
            return FuelErrorCodes.MissingFuels;
        }

        return readings.Select(r => ValidatePrice(r, settings)).FirstOrDefault(error => error is not null);
    }

    public static string? ValidatePrice(FuelReading reading, MarketDataSettings settings)
    {
        if (!InRange(reading.Region1PriceCents, settings) || !InRange(reading.Region2PriceCents, settings))
        {
            return FuelErrorCodes.PriceOutOfRange;
        }

        return reading.Region2PriceCents < reading.Region1PriceCents ? FuelErrorCodes.RegionMismatch : null;
    }

    public static bool ChangeIsPlausible(long previous, long next, MarketDataSettings settings)
        => Math.Abs(next - previous) * 100 <= previous * settings.MaxChangePercent;

    private static bool InRange(long price, MarketDataSettings settings)
        => price >= settings.MinPriceCents && price <= settings.MaxPriceCents;
}

/// <summary>Bảng giá đọc từ một nguồn. Hai trang cùng một đơn vị chỉ tính là một nguồn khi so khớp.</summary>
public sealed record SourceFuelTable(string Domain, IReadOnlyList<FuelReading> Readings);

/// <summary>Kết quả xác nhận giá của một lượt đọc.</summary>
/// <param name="Confirmed">Loại có đủ nguồn khớp và giá khác giá đang lưu.</param>
/// <param name="HeldBack">Loại có đủ nguồn khớp nhưng đổi quá mạnh, không được nhận.</param>
/// <param name="AgreedFuels">Số loại có đủ nguồn khớp, kể cả khi giá không đổi. Bằng 0 nghĩa là lượt này không xác nhận được gì.</param>
public sealed record FuelConfirmation(IReadOnlyList<FuelReading> Confirmed, IReadOnlyList<string> HeldBack, int AgreedFuels);

/// <summary>Chỉ nhận giá có đủ số nguồn cho đúng cùng một cặp số. Nguồn lệch giờ cập nhật thì chờ lượt sau, không chọn bên nào.</summary>
public static class FuelPriceConfirmer
{
    /// <param name="heldStale">Giá mà nguồn đang báo trước khi admin sửa tay. Nguồn còn báo đúng giá cũ đó thì giữ giá admin, không ghi đè.</param>
    public static FuelConfirmation Confirm(
        IReadOnlyList<SourceFuelTable> sourceTables,
        IReadOnlyDictionary<string, FuelReading> latest,
        MarketDataSettings settings,
        IReadOnlyDictionary<string, FuelReading>? heldStale = null)
    {
        var confirmed = new List<FuelReading>();
        var heldBack = new List<string>();
        var agreedFuels = 0;

        var agreed = sourceTables
            .SelectMany(table => table.Readings.Select(reading => (table.Domain, reading)))
            .GroupBy(entry => entry.reading)
            .Where(group => group.Select(entry => entry.Domain).Distinct().Count() >= settings.MinAgreeingSources)
            .Select(group => group.Key)
            .GroupBy(reading => reading.FuelKey);

        foreach (var fuel in agreed)
        {
            // Nhiều nguồn cùng chia làm hai phe bằng nhau: không biết phe nào đúng.
            if (fuel.Count() > 1)
            {
                continue;
            }

            agreedFuels++;
            var reading = fuel.Single();
            if (heldStale is not null && heldStale.TryGetValue(reading.FuelKey, out var stale) && stale == reading)
            {
                continue;
            }

            if (!latest.TryGetValue(reading.FuelKey, out var current))
            {
                confirmed.Add(reading);
            }
            else if (current == reading)
            {
                continue;
            }
            else if (FuelReadingValidator.ChangeIsPlausible(current.Region1PriceCents, reading.Region1PriceCents, settings))
            {
                confirmed.Add(reading);
            }
            else
            {
                heldBack.Add(reading.FuelKey);
            }
        }

        return new FuelConfirmation(confirmed, heldBack, agreedFuels);
    }
}
