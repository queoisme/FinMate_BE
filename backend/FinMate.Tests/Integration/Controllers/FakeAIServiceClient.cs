using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Tests.Integration.Controllers;

// Swap qua DI trong AuthApiFactory thay cho AIServiceClient thật — không gọi AI Service
// thật (chưa tồn tại route /api/v1/analyze, xem TASKS.md Phase 9). Test điều khiển kết
// quả bằng cách đặt sẵn "kịch bản" theo package_name của request.
public class FakeAIServiceClient : IAIServiceClient
{
    private static readonly Dictionary<string, AnalyzeResponse> Scenarios = new();
    private static readonly HashSet<string> UnavailablePackages = new();

    public static void SetScenario(string packageName, AnalyzeResponse response) => Scenarios[packageName] = response;

    public static void SetUnavailable(string packageName) => UnavailablePackages.Add(packageName);

    public Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request, CancellationToken ct = default)
    {
        if (UnavailablePackages.Remove(request.PackageName))
        {
            throw new AIServiceUnavailableException("AI Service unavailable (fake).");
        }

        if (Scenarios.TryGetValue(request.PackageName, out var response))
        {
            return Task.FromResult(response);
        }

        return Task.FromResult(new AnalyzeResponse(
            "non_financial",
            new ClassifierResult("non_financial", 0.99),
            null, null, null, null, 10));
    }

    public Task SendFeedbackAsync(FeedbackRequest request, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Mô phỏng AI Service ĐANG CHẾT: màn hình quản trị phải vẫn trả 200 với phần số liệu của
    /// backend. Đây là hành vi đáng test hơn là nhánh thành công — nhánh hỏng mới là nhánh
    /// người ta quên xử lý.
    /// </summary>
    public Task<AiServiceStats> GetStatsAsync(CancellationToken ct = default)
        => throw new AIServiceUnavailableException("Fake AI Service — luôn không sẵn sàng.");

    /// <summary>
    /// Hóa đơn siêu thị 110.000đ, cố định. Đủ để test tầng HTTP của backend (multipart,
    /// kích thước, quyền) mà không cần binary tesseract trong môi trường test.
    /// </summary>
    public Task<ScanReceiptResponse> ScanReceiptAsync(
        ScanReceiptRequest request, CancellationToken ct = default)
        => Task.FromResult(new ScanReceiptResponse(
            "success",
            new ExtractionResult(
                110_000, "debit", "WINMART+ NGUYEN TRAI", "WINMART+ NGUYEN TRAI",
                new DateTimeOffset(2026, 9, 12, 12, 45, 0, TimeSpan.Zero), null, 0.8),
            new CategorizationResult("shopping", 0.8),
            42));
}
