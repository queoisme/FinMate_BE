using System.Text.Json;
using System.Text.RegularExpressions;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Admin.Commands;

/// <param name="Enabled">Tắt hẳn việc đồng bộ (AI_CATEGORY_SYNC_ENABLED=false), ví dụ trong bộ test tích hợp không có AI Service.</param>
public sealed record AiCategorySyncOptions(bool Enabled);

/// <summary>
/// Đẩy danh mục hệ thống sang AI Service (<c>PUT /api/v1/admin/categories/sync</c>, xem docs/ai-categories-sync-api.md) để AI nhận ngay danh mục admin
/// vừa tạo, đổi tên hoặc tắt mà không cần migration hay deploy lại bên đó. Backend là nguồn sự thật; AI chỉ nhận bản sao.
/// </summary>
public interface IAiCategorySyncService
{
    /// <summary>Đồng bộ và NÉM LỖI khi AI Service không nhận (không phản hồi, từ chối): dành cho job nền, để Hangfire thử lại.</summary>
    Task SyncAsync(Guid? adminId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ nhưng KHÔNG BAO GIỜ ném: dành cho thao tác của admin. Danh mục đã lưu ở DB của backend, nên AI Service tạm hỏng không được làm hỏng việc admin vừa làm;
    /// bản chưa đồng bộ được job định kỳ kéo lại.
    /// </summary>
    /// <returns><c>true</c> khi AI Service đã nhận; <c>false</c> khi đồng bộ bị tắt hoặc thất bại (đã ghi log).</returns>
    Task<bool> TrySyncAsync(Guid? adminId, string? reason, CancellationToken ct = default);
}

public class AiCategorySyncService : IAiCategorySyncService
{
    /// <summary>Người thực hiện khi không có admin nào (job, khởi động): client băm nó như mọi id khác, AI Service chỉ cần một mã băm cố định để ghi nhật ký.</summary>
    public static readonly Guid SystemActorId = new("00000000-0000-0000-0000-00000000a1c5");

    private const string IncomeSlug = "income";
    private const int MaxItemsPerRequest = 200; // giới hạn của AI Service
    private const int MaxLabelChars = 100;
    private const int MaxReasonChars = 500;

    // Quy tắc của AI Service (kb_categories): backend cho phép cả chữ số và tối đa 100 ký tự, nên slug như "pets2" chỉ nằm ở backend.
    private static readonly Regex AiSlug = new("^[a-z_]{1,32}$", RegexOptions.Compiled);

    private readonly ICategoryRepository _categories;
    private readonly IAiRulesAdminClient _client;
    private readonly AiCategorySyncOptions _options;
    private readonly ILogger<AiCategorySyncService> _logger;

    public AiCategorySyncService(
        ICategoryRepository categories, IAiRulesAdminClient client, AiCategorySyncOptions options, ILogger<AiCategorySyncService> logger)
    {
        _categories = categories;
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task SyncAsync(Guid? adminId, string? reason, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var items = new List<Dictionary<string, object>>();
        foreach (var category in await _categories.GetSystemListAsync(includeInactive: true, ct))
        {
            // "Cho vay / Mượn" không phải khoản chi hay thu thông thường (không tính vào tổng chi, tổng thu, ngân sách); AI Service cố ý không giữ nó trong các bảng học.
            if (category.Slug == Category.LoanSlug)
            {
                continue;
            }

            if (!AiSlug.IsMatch(category.Slug) || string.IsNullOrWhiteSpace(category.Name))
            {
                // Một slug AI không nhận được mà gửi cùng lô sẽ làm AI từ chối CẢ lô (422): bỏ riêng nó và báo, các danh mục còn lại vẫn đồng bộ.
                _logger.LogWarning(
                    "System category {Slug} cannot be synced to the AI Service (slug must be 1-32 chars of a-z and underscore, name must not be empty)",
                    category.Slug);
                continue;
            }

            items.Add(new Dictionary<string, object>
            {
                ["slug"] = category.Slug,
                ["label"] = Truncate(category.Name.Trim(), MaxLabelChars),
                ["kind"] = category.Slug == IncomeSlug ? "income" : "expense",
                ["is_active"] = category.IsActive,
            });
        }

        // AI Service từ chối danh sách rỗng; không có gì để đồng bộ thì không gọi.
        foreach (var chunk in items.Chunk(MaxItemsPerRequest))
        {
            var body = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["categories"] = chunk,
                ["reason"] = reason is null ? null : Truncate(reason, MaxReasonChars),
            });

            await _client.SendAsync(
                new AiRulesRequest(HttpMethod.Put, "sync", Body: body, ActorId: adminId ?? SystemActorId, Area: AiRulesRequest.CategoriesArea), ct);
        }
    }

    public async Task<bool> TrySyncAsync(Guid? adminId, string? reason, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        try
        {
            await SyncAsync(adminId, reason, ct);
            return true;
        }
        catch (Exception ex) when (ex is AiRuleException or AIServiceUnavailableException)
        {
            _logger.LogWarning(ex, "System categories were saved but could not be synced to the AI Service; the periodic job will retry");
            return false;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
