using System.Text.Json;

namespace FinMate.Application.Transactions;

/// <summary>
/// Từ vựng hóa đơn dùng chung với ocr-service và ai-service: MỘT tệp <c>ocr-service/data/shared_words.json</c>, nhúng vào assembly lúc build
/// (xem FinMate.Application.csproj; Dockerfile của backend lấy nó từ build context <c>lexicon</c> trong docker-compose). Trước đây ReceiptSplitter chép
/// riêng các danh sách này và chỉ có chú thích "đồng bộ với ocr-service" canh lệch.
/// Mỗi mục là một VAI TRÒ (tax, service, pre_tax...). Thiếu tệp hoặc thiếu vai trò thì lỗi ngay lúc nạp: chạy với danh sách rỗng
/// làm dòng thuế/phí bị coi là một món thường mà không ai biết.
/// </summary>
internal static class SharedWords
{
    private const string ResourceName = "shared_words.json";

    private static readonly Dictionary<string, string[]> Roles = Load();

    /// <summary>Từ của một hay nhiều vai trò, theo thứ tự trong tệp.</summary>
    public static string[] Words(params string[] roles) => roles.SelectMany(role => Roles[role]).ToArray();

    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(SharedWords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên nhúng {ResourceName}: build backend cần ocr-service/data/{ResourceName}.");
        using var document = JsonDocument.Parse(stream);

        var roles = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("words", out var words))
            {
                roles[property.Name] = words.EnumerateArray().Select(word => word.GetString()!).ToArray();
            }
        }

        return roles;
    }
}
