namespace FinMate.Application.Common.Interfaces;

public enum WebFetchOutcome
{
    Ok,

    /// <summary>URL không phải <c>https</c> hoặc trỏ tới địa chỉ nội bộ.</summary>
    AddressNotAllowed,

    /// <summary>Trang từ chối bằng 401, 403, 429. Không tìm cách vượt.</summary>
    Refused,
    NotFound,
    TooLarge,
    UnsupportedContent,
    TooManyRedirects,
    Failed,
}

public sealed record WebFetchResult(WebFetchOutcome Outcome, string? Content = null, string? ContentType = null, Uri? FinalUri = null, int? StatusCode = null);

/// <summary>Tải trang web do admin đưa vào. Nơi duy nhất backend mở kết nối tới một URL không cố định, nên phải chống SSRF ở đây.</summary>
public interface IWebPageFetcher
{
    Task<WebFetchResult> FetchAsync(Uri uri, CancellationToken ct = default);
}
