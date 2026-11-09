using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Interfaces;

public sealed record MarketSignalSource(string Title, string Url, string Evidence, decimal? ChangePercent, DateOnly? EffectiveDate, DateTimeOffset? PublishedAt, string SourceName);

public interface IMarketDataRepository
{
    Task<List<MarketSource>> ListSourcesAsync(CancellationToken ct = default);
    Task<List<MarketSource>> ListActiveSourcesAsync(CancellationToken ct = default);
    Task<MarketSource?> GetSourceAsync(Guid id, CancellationToken ct = default);

    /// <summary>False nếu URL đã có, kể cả khi hai request chạy cùng lúc.</summary>
    Task<bool> TryAddSourceAsync(MarketSource source, CancellationToken ct = default);
    Task<HashSet<string>> FindExistingDocumentUrlsAsync(IReadOnlyCollection<string> urls, CancellationToken ct = default);

    /// <summary>False nếu bài đã có, khi nguồn khác vừa đọc cùng link đó.</summary>
    Task<bool> TryAddDocumentAsync(MarketDocument document, CancellationToken ct = default);
    Task<(List<MarketDocument> Items, int Total)> ListDocumentsAsync(Guid? sourceId, MarketDocumentStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<int> DeleteDocumentsBeforeAsync(DateTimeOffset before, CancellationToken ct = default);
    Task<List<FuelPrice>> ListLatestFuelPricesAsync(CancellationToken ct = default);
    Task<(List<FuelPrice> Items, int Total)> ListFuelPricesAsync(string? fuelKey, int offset, int limit, CancellationToken ct = default);
    void AddFuelPrice(FuelPrice price);
    Task<int> MarkPendingDocumentsNotAllowedAsync(CancellationToken ct = default);
    Task<List<MarketDocument>> ListDocumentsForAiAsync(int limit, CancellationToken ct = default);
    Task<int> CountAiAttemptsSinceAsync(DateTimeOffset since, CancellationToken ct = default);
    void AddSignal(MarketSignal signal);

    /// <summary>Bỏ các tín hiệu chưa lưu được, để lần lưu sau không gặp lại đúng lỗi đó.</summary>
    void DiscardUnsavedSignals();

    /// <summary>Giá ngay trước mốc đó.</summary>
    Task<FuelPrice?> GetFuelPriceBeforeAsync(string fuelKey, DateTimeOffset at, Guid exceptId, CancellationToken ct = default);

    /// <summary>Giá gần nhất do nguồn báo, không tính giá admin sửa tay.</summary>
    Task<FuelPrice?> GetLatestSourceFuelPriceAsync(string fuelKey, CancellationToken ct = default);
    Task<List<FuelPrice>> ListFuelPricesAwaitingAlertsAsync(string fuelKey, CancellationToken ct = default);
    Task<List<MarketEvent>> ListEventsAwaitingAlertsAsync(CancellationToken ct = default);

    /// <summary>Giá đang áp dụng tại mốc đó. Chưa có giá nào từ trước mốc thì lấy dòng cũ nhất, null nếu chưa có dòng nào.</summary>
    Task<FuelPrice?> GetFuelPriceAsOfAsync(string fuelKey, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Nguồn và ngày đăng của các tín hiệu đã xảy ra. Bài không rõ ngày đăng không được tính, vì có thể là tin cũ.</summary>
    Task<List<(string SourceUrl, DateTimeOffset SeenAt)>> ListActualSignalSourcesAsync(string topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default);
    Task<MarketEvent?> GetLatestEventAsync(string topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>False nếu tiến trình khác vừa tạo sự kiện đang mở cùng chủ đề và hướng.</summary>
    Task<bool> TryAddEventAsync(MarketEvent marketEvent, CancellationToken ct = default);
    Task<int> ExpireEventsAsync(DateTimeOffset lastSeenBefore, CancellationToken ct = default);
    Task<(List<MarketEvent> Items, int Total)> ListEventsAsync(MarketEventStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<List<MarketSignalSource>> ListEventSignalsAsync(string topic, MarketDirection direction, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Chỉ đổi khi sự kiện còn chờ duyệt, nên hai admin cùng bấm thì một người nhận false.</summary>
    Task<bool> TryReviewEventAsync(Guid id, MarketEventStatus status, Guid adminId, string? note, DateTimeOffset now, CancellationToken ct = default);
    Task<MarketEvent?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
