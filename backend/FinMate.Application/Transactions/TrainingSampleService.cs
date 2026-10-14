using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Transactions;

/// <summary>
/// Gửi chữ hoá đơn ĐÃ lọc (từ OCR service, không ảnh) và nhãn cuối sang ai-service để học, CHỈ khi người dùng đang đồng ý đóng góp dữ liệu
/// (đúng phiên bản, đọc lại từ DB mỗi lần để việc rút đồng ý có hiệu lực ngay) và nhãn là danh mục hệ thống.
/// Là phần phụ chạy sau khi giao dịch đã lưu nên không bao giờ ném lỗi; log chỉ ghi tên loại lỗi, không ghi chữ, tên cửa hàng, tiền hay danh mục.
/// </summary>
public interface ITrainingSampleService
{
    Task RecordAsync(
        Guid userId, Guid scanId, string? merchantName, IReadOnlyList<string> lines, long? totalAmount,
        string? predictedSlug, string? predictedSource, CancellationToken ct = default);

    /// <summary>Nhãn cuối là <paramref name="category"/>; null, danh mục tự tạo hoặc thu nhập thì bỏ qua (AI chỉ biết slug hệ thống).</summary>
    Task LabelAsync(Guid userId, Guid scanId, Category? category, CancellationToken ct = default);
}

public class TrainingSampleService : ITrainingSampleService
{
    private const string IncomeSlug = "income";

    private readonly IUserRepository _userRepository;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ILogger<TrainingSampleService> _logger;

    public TrainingSampleService(IUserRepository userRepository, IAIServiceClient aiServiceClient, ILogger<TrainingSampleService> logger)
    {
        _userRepository = userRepository;
        _aiServiceClient = aiServiceClient;
        _logger = logger;
    }

    public async Task RecordAsync(
        Guid userId, Guid scanId, string? merchantName, IReadOnlyList<string> lines, long? totalAmount,
        string? predictedSlug, string? predictedSource, CancellationToken ct = default)
    {
        if (lines.Count == 0)
        {
            return;
        }

        await RunAsync(userId, () => _aiServiceClient.RecordTrainingSampleAsync(
            new TrainingSampleRequest(scanId, userId, merchantName, lines, totalAmount, predictedSlug, predictedSource), ct), ct);
    }

    public async Task LabelAsync(Guid userId, Guid scanId, Category? category, CancellationToken ct = default)
    {
        if (category is not { UserId: null } system || system.Slug == IncomeSlug)
        {
            return;
        }

        await RunAsync(userId, () => _aiServiceClient.LabelTrainingSampleAsync(new TrainingLabelRequest(scanId, userId, system.Slug), ct), ct);
    }

    private async Task RunAsync(Guid userId, Func<Task> send, CancellationToken ct)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user is { TrainingConsentGranted: true })
            {
                await send();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Training sample step failed after the transaction was saved: {ExceptionType}", ex.GetType().Name);
        }
    }
}
