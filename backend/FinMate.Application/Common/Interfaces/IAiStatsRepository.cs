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
    int? MaxProcessingMs,
    DuplicateOutcomeStats? Duplicates = null);

/// <summary>Số lượng của một cặp nguồn (Phase 82). Chỉ là số đếm: không id, không số tiền, không gì theo user.</summary>
public record DuplicatePairCounts(int Suspected, int Dismissed, int Merged);

/// <param name="ByPair">
/// Khoá = hai nguồn chữ thường, sắp theo bảng chữ cái, nối bằng <c>+</c> (vd <c>manual+notification</c>). <c>Suspected</c>/<c>Dismissed</c> đếm theo
/// trạng thái HIỆN TẠI của các giao dịch tạo trong kỳ; <c>Merged</c> đếm bản bị gộp có thời điểm gộp trong kỳ.
/// </param>
/// <param name="SameEventDropped">Thông báo bị bỏ vì là cùng một sự kiện ngân hàng gửi lại (log <c>duplicate</c>, BR-DUP-05).</param>
public record DuplicateOutcomeStats(IReadOnlyDictionary<string, DuplicatePairCounts> ByPair, int SameEventDropped);

public interface IAiStatsRepository
{
    /// <summary>Nửa mở <c>[from, to)</c> trên <c>ai_results.created_at</c>.</summary>
    Task<AiProductionStats> GetProductionStatsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
