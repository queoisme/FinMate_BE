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
}
