using FinMate.Application.Economics;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>Tải trang công khai của nguồn số liệu. Lỗi mạng hay mã khác 2xx trả null để bộ làm mới báo sự cố thay vì ném lỗi.</summary>
public sealed class EconomicFeedClient(HttpClient http) : IEconomicFeedClient
{
    // Trang thống kê chỉ vài trăm KB; trần này chặn một địa chỉ cấu hình sai trỏ vào tệp khổng lồ.
    private const int MaxBytes = 2_000_000;

    public async Task<string?> GetPageAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > MaxBytes ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;   // hết hạn chờ
        }
    }
}
