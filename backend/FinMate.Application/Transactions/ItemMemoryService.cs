using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Transactions;

/// <summary>
/// Ghi nhận danh mục người dùng CHỐT cho từng món của một hóa đơn đã tách, để lần sau món cùng tên được gợi ý đúng danh mục đó (kể cả danh mục tự tạo).
/// Chỉ áp dụng cho lần quét TÁCH; danh mục cuối là thu nhập, cho vay hoặc không có thì bỏ qua. Mỗi lần gọi gửi tối đa 60 món (giới hạn của ai-service).
/// Là phần phụ chạy sau khi giao dịch đã lưu nên hai hàm <c>Learn*</c> không bao giờ ném; log chỉ ghi tên loại lỗi, không ghi tên món, tiền hay danh mục.
/// </summary>
public interface IItemMemoryService
{
    /// <summary>Người dùng vừa lưu nhóm <paramref name="group"/> của lần quét với danh mục cuối <paramref name="finalCategory"/>.</summary>
    Task LearnFromSavedGroupAsync(Guid userId, Guid scanId, string group, Category? finalCategory, CancellationToken ct = default);

    /// <summary>Người dùng đổi danh mục của giao dịch đã lưu; nhóm tìm qua dòng nối. Giao dịch không nối với lần quét nào: không làm gì.</summary>
    Task LearnFromCorrectionAsync(Guid userId, Guid transactionId, Category? newCategory, CancellationToken ct = default);

    /// <summary>True khi ai-service xác nhận đã xóa toàn bộ bộ nhớ món. False khi chưa xóa được (không ném): người gọi phải thử lại, không được báo "đã xóa".</summary>
    Task<bool> EraseAsync(Guid userId, CancellationToken ct = default);
}

public class ItemMemoryService : IItemMemoryService
{
    /// <summary>Giới hạn của ai-service cho một lần ghi (vượt thì 422, mà lời gọi best-effort nuốt lỗi).</summary>
    private const int MaxItemsPerCall = 60;

    private readonly IReceiptScanRepository _receiptScans;
    private readonly IAIServiceClient _aiServiceClient;
    private readonly ILogger<ItemMemoryService> _logger;

    public ItemMemoryService(IReceiptScanRepository receiptScans, IAIServiceClient aiServiceClient, ILogger<ItemMemoryService> logger)
    {
        _receiptScans = receiptScans;
        _aiServiceClient = aiServiceClient;
        _logger = logger;
    }

    public async Task LearnFromSavedGroupAsync(Guid userId, Guid scanId, string group, Category? finalCategory, CancellationToken ct = default)
    {
        try
        {
            await LearnAsync(userId, scanId, group, finalCategory, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Recording item categories failed after the transaction was saved: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task LearnFromCorrectionAsync(Guid userId, Guid transactionId, Category? newCategory, CancellationToken ct = default)
    {
        try
        {
            var link = await _receiptScans.GetLinkedGroupAsync(transactionId, userId, ct);
            if (link is null)
            {
                return;
            }

            await LearnAsync(userId, link.Value.ScanId, link.Value.Group, newCategory, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Recording item categories after a correction failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    public async Task<bool> EraseAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await _aiServiceClient.EraseItemMemoryAsync(userId, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Erasing item memory failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private async Task LearnAsync(Guid userId, Guid scanId, string group, Category? finalCategory, CancellationToken ct)
    {
        // Thu nhập và cho vay nhận diện theo slug (Category chưa có cột loại); danh mục của chính người dùng (UserId != null) thì được học.
        if (finalCategory is null || finalCategory.Slug == TrainingSampleService.IncomeSlug || finalCategory.Slug == Category.LoanSlug)
        {
            return;
        }

        if (!await _receiptScans.IsSplitAsync(scanId, userId, ct))
        {
            return;
        }

        // Nhóm "unknown" không phải danh mục AI đề xuất nên luôn là Corrected, kể cả khi slug danh mục cuối trùng chữ.
        var kind = group != ReceiptScanLink.UnknownGroup && finalCategory.Slug == group ? ItemMemoryKinds.Accepted : ItemMemoryKinds.Corrected;
        var items = (await _receiptScans.GetGroupItemsAsync(scanId, userId, group, ct))
            .OrderBy(i => i.Position)
            .Take(MaxItemsPerCall)
            .Select(i => new LearnItemEntry(i.Name, finalCategory.Id, kind))
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        await _aiServiceClient.LearnItemsAsync(new LearnItemsRequest(userId, items), ct);
    }
}
