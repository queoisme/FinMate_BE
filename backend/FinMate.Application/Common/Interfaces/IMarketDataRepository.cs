using FinMate.Domain.Entities;

namespace FinMate.Application.Common.Interfaces;

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
    Task<(List<MarketDocument> Items, int Total)> ListDocumentsAsync(Guid? sourceId, Domain.Enums.MarketDocumentStatus? status, int offset, int limit, CancellationToken ct = default);
    Task<int> DeleteDocumentsBeforeAsync(DateTimeOffset before, CancellationToken ct = default);
    Task<List<FuelPrice>> ListLatestFuelPricesAsync(CancellationToken ct = default);
    Task<(List<FuelPrice> Items, int Total)> ListFuelPricesAsync(string? fuelKey, int offset, int limit, CancellationToken ct = default);
    void AddFuelPrice(FuelPrice price);
    Task SaveChangesAsync(CancellationToken ct = default);
}
