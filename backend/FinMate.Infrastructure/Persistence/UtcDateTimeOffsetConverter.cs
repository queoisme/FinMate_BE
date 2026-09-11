using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinMate.Infrastructure.Persistence;

/// <summary>
/// Đưa <see cref="DateTimeOffset"/> về offset 0 khi ghi, giữ nguyên khi đọc.
/// Xem <see cref="FinMateDbContext.ConfigureConventions"/> cho lý do đầy đủ.
/// </summary>
public class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime(),
            // Npgsql đọc timestamptz ra sẵn ở offset 0 nên chiều đọc không cần đổi gì.
            value => value)
    {
    }
}
