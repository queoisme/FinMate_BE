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

    /// <summary>Kiểm tra giá đọc từ thông cáo của Bộ Công Thương: phải có đủ <see cref="FuelKeys.ReleaseRequired"/>.</summary>
    public static string? ValidateRelease(IReadOnlyList<FuelReading> readings, MarketDataSettings settings)
    {
        if (FuelKeys.ReleaseRequired.Any(key => readings.All(r => r.FuelKey != key)))
        {
            return FuelErrorCodes.MissingFuels;
        }

        return readings.Select(r => ValidatePrice(r, settings)).FirstOrDefault(error => error is not null);
    }

    public static string? ValidatePrice(FuelReading reading, MarketDataSettings settings)
    {
        if (!InRange(reading.Region1PriceCents, settings) || (reading.Region2PriceCents is { } region2 && !InRange(region2, settings)))
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

/// <summary>Bảng giá đọc từ một nguồn.</summary>
/// <param name="Official">Nguồn hạng official: tự nó đủ để xác nhận, và thắng nguồn có giấy phép khi hai bên khác nhau.</param>
/// <param name="Owner">Đơn vị chủ quản. Hai nguồn cùng chủ quản (hoặc cùng tên miền khi không biết chủ quản) chỉ tính là một khi so khớp.</param>
public sealed record SourceFuelTable(string Domain, IReadOnlyList<FuelReading> Readings, bool Official = false, string? Owner = null);

/// <summary>Kết quả xác nhận giá của một lượt đọc.</summary>
/// <param name="Confirmed">Loại có đủ nguồn khớp và giá khác giá đang lưu.</param>
/// <param name="HeldBack">Loại có đủ nguồn khớp nhưng đổi quá mạnh, không được nhận.</param>
/// <param name="AgreedFuels">Số loại có đủ nguồn khớp, kể cả khi giá không đổi. Bằng 0 nghĩa là lượt này không xác nhận được gì.</param>
/// <param name="Domains">Tên miền đã cho đúng giá được nhận, theo mã giá.</param>
public sealed record FuelConfirmation(IReadOnlyList<FuelReading> Confirmed, IReadOnlyList<string> HeldBack, int AgreedFuels, IReadOnlyDictionary<string, IReadOnlyList<string>>? Domains = null);

/// <summary>
/// Một nguồn official là đủ; không có thì phải có đủ số đơn vị chủ quản khác nhau cho đúng cùng một cặp số.
/// Nguồn lệch giờ cập nhật thì chờ lượt sau, không chọn bên nào.
/// </summary>
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
        var domains = new Dictionary<string, IReadOnlyList<string>>();
        var agreedFuels = 0;

        var entries = sourceTables.SelectMany(table => table.Readings.Select(reading => (Table: table, Reading: reading))).ToList();
        foreach (var fuel in entries.GroupBy(entry => entry.Reading.FuelKey))
        {
            var official = fuel.Where(entry => entry.Table.Official).ToList();
            var candidates = official.Count > 0
                ? official.GroupBy(entry => entry.Reading).ToList()
                : fuel.GroupBy(entry => entry.Reading)
                    .Where(group => group.Select(entry => entry.Table.Owner ?? entry.Table.Domain).Distinct().Count() >= settings.MinAgreeingSources)
                    .ToList();

            // Không phe nào đủ, hoặc nhiều phe cùng đủ (kể cả hai nguồn official khác nhau): không biết phe nào đúng.
            if (candidates.Count != 1)
            {
                continue;
            }

            agreedFuels++;
            var reading = candidates[0].Key;
            var agreedDomains = candidates[0].Select(entry => entry.Table.Domain).Distinct().ToList();
            if (heldStale is not null && heldStale.TryGetValue(reading.FuelKey, out var stale) && SamePrice(stale, reading))
            {
                continue;
            }

            if (!latest.TryGetValue(reading.FuelKey, out var current))
            {
                confirmed.Add(reading);
            }
            else if (SamePrice(current, reading))
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
                continue;
            }

            domains[reading.FuelKey] = agreedDomains;
        }

        return new FuelConfirmation(confirmed, heldBack, agreedFuels, domains);
    }

    /// <summary>Nguồn không có giá vùng 2 thì chỉ so vùng 1, để đổi từ bảng giá sang thông cáo không tạo ra một "lần đổi giá" giả.</summary>
    private static bool SamePrice(FuelReading stored, FuelReading reading)
        => stored.Region1PriceCents == reading.Region1PriceCents && (reading.Region2PriceCents is null || stored.Region2PriceCents == reading.Region2PriceCents);
}
