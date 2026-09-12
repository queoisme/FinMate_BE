using System.Globalization;
using System.Text;

namespace FinMate.Infrastructure.Persistence.Repositories;

/// <summary>
/// Mã hoá con trỏ phân trang keyset dạng <c>(thời điểm, id)</c>.
///
/// Keyset chứ không phải OFFSET: danh sách admin sắp theo thời gian giảm dần và có dữ liệu
/// chèn vào liên tục, nên OFFSET sẽ lặp hoặc bỏ sót bản ghi giữa hai trang.
///
/// Khoá phụ là <c>Id</c> và chỉ dùng để PHÁ HOÀ khi hai bản ghi trùng mốc thời gian — thứ tự
/// vẫn do thời gian quyết định. Đây là chỗ khác với <c>TransactionRepository</c>, nơi khoá phụ
/// là <c>created_at</c>; ghi chú ở đó đã nói rõ không so sánh <c>Guid</c> bằng <c>&lt;</c>/<c>&gt;</c>
/// làm khoá sắp xếp chính vì Npgsql dịch không đáng tin.
/// </summary>
public static class KeysetCursor
{
    public static string Encode(DateTimeOffset at, Guid id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at:O}|{id}"));

    public static bool TryDecode(string? cursor, out DateTimeOffset at, out Guid id)
    {
        at = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2
                && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at)
                && Guid.TryParse(parts[1], out id);
        }
        catch (FormatException)
        {
            // Con trỏ do client gửi lên — hỏng thì coi như không có, trả về trang đầu thay vì 500.
            return false;
        }
    }
}
