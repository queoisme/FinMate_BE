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

    /// <summary>Xóa mọi mẫu đã đóng góp của người dùng. KHÔNG đòi đang đồng ý (thường gọi ngay sau khi họ rút đồng ý hoặc khi xóa tài khoản).</summary>
    /// <summary>True khi ai-service xác nhận đã xóa. False khi chưa xóa được (không ném lỗi): người gọi KHÔNG được báo "đã xóa" và phải thử lại sau.</summary>
    Task<bool> EraseAsync(Guid userId, CancellationToken ct = default);
}

public class TrainingSampleService : ITrainingSampleService
{
    private const string IncomeSlug = "income";
    private const int MaxMerchantChars = 200;
    private const int MaxLines = 200;

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

        // Cắt theo giới hạn của ai-service (BillSampleRequest): vượt thì nó trả 422 và lời gọi best-effort nuốt lỗi, mẫu mất mà không ai biết.
        var clippedMerchant = merchantName is { Length: > MaxMerchantChars } ? merchantName[..MaxMerchantChars] : merchantName;
        var clippedLines = lines.Count > MaxLines ? lines.Take(MaxLines).ToList() : lines;

        var sent = await RunAsync(userId, () => _aiServiceClient.RecordTrainingSampleAsync(
            new TrainingSampleRequest(scanId, userId, clippedMerchant, clippedLines, totalAmount, predictedSlug, predictedSource), ct), ct);

        // Rút đồng ý đúng lúc lời gọi đang bay: lần xóa của việc rút có thể đã chạy xong TRƯỚC khi mẫu này đến nơi. Kiểm lại và xóa nếu cần.
        if (sent && !await StillConsentedAsync(userId, ct))
        {
            await EraseAsync(userId, ct);
        }
    }

    public async Task LabelAsync(Guid userId, Guid scanId, Category? category, CancellationToken ct = default)
    {
        if (category is null)
        {
            return;
        }

        if (category is not { UserId: null } system || system.Slug == IncomeSlug)
        {
            // Danh mục tự tạo hoặc thu nhập: nhãn hệ thống đã gắn trước đó là nhãn người dùng vừa bác bỏ, không được giữ lại làm dữ liệu học.
            await RunAsync(userId, () => _aiServiceClient.DiscardTrainingSampleAsync(scanId, userId, ct), ct);
            return;
        }

        await RunAsync(userId, () => _aiServiceClient.LabelTrainingSampleAsync(new TrainingLabelRequest(scanId, userId, system.Slug), ct), ct);
    }

    public async Task<bool> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await _aiServiceClient.EraseTrainingSamplesAsync(userId, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Erasing training samples failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> StillConsentedAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            return await _userRepository.GetByIdAsync(userId, ct) is { TrainingConsentGranted: true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Re-checking training consent failed: {ExceptionType}", ex.GetType().Name);
            return true;  // không biết thì không xóa: lần rút kế tiếp hoặc việc dọn mẫu chờ nhãn sẽ xử lý
        }
    }

    /// <returns>True khi đã gửi (đang đồng ý và lời gọi không lỗi).</returns>
    private async Task<bool> RunAsync(Guid userId, Func<Task> send, CancellationToken ct)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId, ct);
            if (user is { TrainingConsentGranted: true })
            {
                await send();
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Training sample step failed after the transaction was saved: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }
}
