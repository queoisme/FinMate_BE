namespace FinMate.Application.Common.Interfaces;

/// <param name="CategoryCorrections">
/// Số giao dịch do AI tạo ra mà người dùng đã đổi sang danh mục khác. Đây là thước đo CHẤT
/// LƯỢNG THẬT của Categorizer và nó chỉ tồn tại ở backend DB: AI DB thấy dự đoán của chính nó
/// nhưng không bao giờ thấy người dùng làm gì với dự đoán đó.
/// </param>
/// <param name="CategorisedTransactions">
/// Mẫu số của tỉ lệ trên — giao dịch từ notification, đã confirm, mà AI có đoán danh mục.
/// </param>
public record AiProductionStats(
    int TotalAnalyzed,
    IReadOnlyDictionary<string, int> ByPipelineResult,
    IReadOnlyDictionary<string, int> ByPackageName,
    IReadOnlyDictionary<string, int> ByClassifierVersion,
    int PotentialDuplicates,
    int DraftsCreated,
    int DraftsConfirmed,
    int CategoryCorrections,
    int CategorisedTransactions,
    double? AvgClassifierConfidence,
    double? AvgExtractionConfidence,
    double? AvgCategorizationConfidence,
    double? AvgProcessingMs,
    int? MaxProcessingMs);

public interface IAiStatsRepository
{
    /// <summary>Nửa mở <c>[from, to)</c> trên <c>ai_results.created_at</c>.</summary>
    Task<AiProductionStats> GetProductionStatsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
