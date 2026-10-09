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

    private static readonly Dictionary<string, ChatParseResponse> ChatScenarios = new();

    /// <summary>Đặt sẵn câu trả lời của <c>/chat/parse</c> theo đúng câu chat gửi lên (mỗi test dùng một câu riêng).</summary>
    public static void SetChat(string text, ChatParseResponse response) => ChatScenarios[text] = response;

    public Task<ChatParseResponse> ParseChatAsync(ChatParseRequest request, CancellationToken ct = default)
        => Task.FromResult(ChatScenarios.TryGetValue(request.Text, out var response)
            ? response
            : new ChatParseResponse("out_of_scope", "Mình chỉ giúp ghi chi tiêu.", null, null, 5));

    private static readonly List<LearnMerchantRequest> LearnedChoices = new();

    /// <summary>Những lần backend nhờ AI nhớ cửa hàng người dùng đã chốt (test lọc theo tên cửa hàng riêng của mình).</summary>
    public static IReadOnlyList<LearnMerchantRequest> Learned(string merchantName)
        => LearnedChoices.Where(l => l.MerchantName == merchantName).ToList();

    public Task LearnMerchantAsync(LearnMerchantRequest request, CancellationToken ct = default)
    {
        LearnedChoices.Add(request);
        return Task.CompletedTask;
    }

    private static readonly Dictionary<string, BillClassifyResponse> BillScenarios = new();

    /// <summary>Đặt sẵn kết quả <c>/bill/classify</c> theo tên cửa hàng gửi lên; không có thì "không nhận ra".</summary>
    public static void SetBill(string merchantName, BillClassifyResponse response) => BillScenarios[merchantName] = response;

    public Task<BillClassifyResponse> ClassifyBillAsync(BillClassifyRequest request, CancellationToken ct = default)
        => Task.FromResult(request.MerchantName is not null && BillScenarios.TryGetValue(request.MerchantName, out var response)
            ? response
            : new BillClassifyResponse(null, null, 0.0, "none"));

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
